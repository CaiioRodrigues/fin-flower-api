namespace FinFlower.Infrastructure.Reports;

/// <summary>
/// A flor da marca em SVG, com as cores fixas: o PDF não tem variável de CSS,
/// e o documento precisa sair igual em qualquer tema do aplicativo.
/// </summary>
internal static class BrandMark
{
    public const string Svg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
          <path d="M24 20 L24 43" stroke="#15803d" stroke-width="2.8" stroke-linecap="round" />
          <path d="M24 33.5 C29.5 31, 34 33.5, 34 33.5 C31.5 38, 26 38, 24 33.5 Z" fill="#22a355" />
          <path d="M24 38.5 C18.5 36, 14 38.5, 14 38.5 C16.5 43, 22 43, 24 38.5 Z" fill="#15803d" />
          <g transform="translate(24 19)">
            <g fill="#2563eb">
              <ellipse cx="0" cy="-9.5" rx="5.6" ry="8.4" />
              <ellipse cx="0" cy="-9.5" rx="5.6" ry="8.4" transform="rotate(144)" />
              <ellipse cx="0" cy="-9.5" rx="5.6" ry="8.4" transform="rotate(288)" />
            </g>
            <g fill="#4f83f1">
              <ellipse cx="0" cy="-9.5" rx="5.6" ry="8.4" transform="rotate(72)" />
              <ellipse cx="0" cy="-9.5" rx="5.6" ry="8.4" transform="rotate(216)" />
            </g>
            <circle r="6.2" fill="#ffffff" />
            <circle r="6.2" fill="none" stroke="#1d4ed8" stroke-width="1.8" />
            <circle r="2.1" fill="#1d4ed8" />
          </g>
        </svg>
        """;
}
