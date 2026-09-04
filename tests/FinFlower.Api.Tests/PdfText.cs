using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace FinFlower.Api.Tests;

/// <summary>
/// Lê o texto de um PDF gerado pelo QuestPDF.
///
/// Existe porque a alternativa era não conferir. O QuestPDF comprime o fluxo de
/// conteúdo e escreve as palavras como códigos de glifo de uma fonte embutida,
/// então procurar a frase nos bytes crus nunca acha nada — e um teste que
/// procura no lugar errado passa a impressão de estar cuidando do documento sem
/// olhar para ele uma única vez.
///
/// O caminho aqui é o mesmo de qualquer leitor: descompacta os fluxos, monta o
/// mapa código → caractere a partir dos /ToUnicode e traduz o que os operadores
/// de texto desenham. Se algum código ficar de fora do mapa, ele vira '?' — o
/// erro aparece como asserção que não encontra a frase, nunca como um teste que
/// passa sem ter lido nada.
/// </summary>
internal static class PdfText
{
    private static readonly Regex HexString = new(@"<([0-9A-Fa-f]+)>", RegexOptions.Compiled);

    private static readonly Regex ShowText = new(
        @"\[(?<array>[^\]]*)\]\s*TJ|(?<single><[0-9A-Fa-f]+>)\s*Tj",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex BfChar = new(
        @"beginbfchar(?<body>.*?)endbfchar", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex BfRange = new(
        @"beginbfrange(?<body>.*?)endbfrange", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex CharMapping = new(
        @"<(?<from>[0-9A-Fa-f]+)>\s*<(?<to>[0-9A-Fa-f]+)>", RegexOptions.Compiled);

    private static readonly Regex RangeMapping = new(
        @"<(?<low>[0-9A-Fa-f]+)>\s*<(?<high>[0-9A-Fa-f]+)>\s*<(?<base>[0-9A-Fa-f]+)>",
        RegexOptions.Compiled);

    /// <summary>
    /// Uma linha por trecho desenhado, na ordem em que o documento os escreve.
    /// </summary>
    public static string Extract(byte[] pdf)
    {
        var streams = Inflate(pdf);
        var glyphs = BuildGlyphMap(streams);
        var text = new StringBuilder();

        foreach (var stream in streams)
        {
            foreach (Match show in ShowText.Matches(stream))
            {
                var source = show.Groups["array"].Success
                    ? show.Groups["array"].Value
                    : show.Groups["single"].Value;

                var line = new StringBuilder();
                foreach (Match hex in HexString.Matches(source))
                    line.Append(Decode(hex.Groups[1].Value, glyphs));

                if (line.Length > 0)
                    text.AppendLine(line.ToString());
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Todo fluxo do arquivo que o zlib consiga abrir. Os que não abrem são
    /// imagens e tabelas de fonte — não carregam texto e não fazem falta.
    /// </summary>
    private static List<string> Inflate(byte[] pdf)
    {
        const string Begin = "stream";
        const string End = "endstream";

        var raw = Encoding.Latin1.GetString(pdf);
        var streams = new List<string>();
        var cursor = 0;

        while (true)
        {
            var start = raw.IndexOf(Begin, cursor, StringComparison.Ordinal);
            if (start < 0) break;

            var payload = start + Begin.Length;
            if (payload < raw.Length && raw[payload] == '\r') payload++;
            if (payload < raw.Length && raw[payload] == '\n') payload++;

            var end = raw.IndexOf(End, payload, StringComparison.Ordinal);
            if (end < 0) break;

            cursor = end + End.Length;

            var bytes = Encoding.Latin1.GetBytes(raw[payload..end]);
            try
            {
                using var source = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
                using var target = new MemoryStream();
                source.CopyTo(target);
                streams.Add(Encoding.Latin1.GetString(target.ToArray()));
            }
            catch (InvalidDataException)
            {
                // Fluxo que não é zlib — não é o texto que estamos procurando.
            }
        }

        return streams;
    }

    /// <summary>
    /// Junta os /ToUnicode de todas as fontes num mapa só. Elas são recortes da
    /// mesma família e mantêm os índices originais dos glifos, então os códigos
    /// coincidem; se um dia deixassem de coincidir, a frase sairia embaralhada e
    /// a asserção falharia — que é o lado seguro do erro.
    /// </summary>
    private static Dictionary<string, string> BuildGlyphMap(IEnumerable<string> streams)
    {
        var glyphs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var stream in streams)
        {
            foreach (Match block in BfChar.Matches(stream))
            {
                foreach (Match mapping in CharMapping.Matches(block.Groups["body"].Value))
                {
                    glyphs[mapping.Groups["from"].Value] =
                        FromUtf16Be(mapping.Groups["to"].Value);
                }
            }

            foreach (Match block in BfRange.Matches(stream))
            {
                foreach (Match mapping in RangeMapping.Matches(block.Groups["body"].Value))
                {
                    var low = Convert.ToInt32(mapping.Groups["low"].Value, 16);
                    var high = Convert.ToInt32(mapping.Groups["high"].Value, 16);
                    var start = Convert.ToInt32(mapping.Groups["base"].Value, 16);

                    for (var code = low; code <= high; code++)
                        glyphs[code.ToString("x4", CultureInfo.InvariantCulture)] = char.ConvertFromUtf32(start + code - low);
                }
            }
        }

        return glyphs;
    }

    private static string Decode(string hex, Dictionary<string, string> glyphs)
    {
        if (hex.Length % 4 != 0) return string.Empty;

        var decoded = new StringBuilder(hex.Length / 4);
        for (var i = 0; i < hex.Length; i += 4)
            decoded.Append(glyphs.GetValueOrDefault(hex.Substring(i, 4), "?"));

        return decoded.ToString();
    }

    private static string FromUtf16Be(string hex)
    {
        var bytes = Convert.FromHexString(hex.Length % 2 == 0 ? hex : "0" + hex);
        return Encoding.BigEndianUnicode.GetString(bytes);
    }
}
