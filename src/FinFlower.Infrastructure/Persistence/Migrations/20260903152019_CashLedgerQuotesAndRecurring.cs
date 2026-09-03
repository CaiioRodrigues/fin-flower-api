using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlower.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashLedgerQuotesAndRecurring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Entries_Events_EventId",
                table: "Entries");

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "Entries",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Entries",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "RecurringItemId",
                table: "Entries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurringMonth",
                table: "Entries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Entries",
                type: "int",
                nullable: false,
                defaultValue: 1);

            // Os lançamentos que já existem nasceram dentro de um evento e não
            // têm dono próprio. Sem este preenchimento eles ficariam com
            // OwnerId zerado — invisíveis ao dono, e recusados pela chave
            // estrangeira para Users criada mais abaixo nesta mesma migração.
            migrationBuilder.Sql("""
                UPDATE e
                SET e.OwnerId = v.OwnerId
                FROM Entries e
                INNER JOIN Events v ON v.Id = e.EventId;
                """);

            // Source é o novo enum de origem: 1 = manual, 2 = contrato. O que
            // veio de parcela é reconhecível pelo vínculo que já estava lá.
            migrationBuilder.Sql("""
                UPDATE Entries
                SET Source = 2
                WHERE InstallmentId IS NOT NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "Contracts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "QuoteId",
                table: "Contracts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Quotes_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    DayOfMonth = table.Column<int>(type: "int", nullable: false),
                    StartMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    EndMonth = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringItems_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuoteItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteItems_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_InstallmentId",
                table: "Entries",
                column: "InstallmentId",
                unique: true,
                filter: "[InstallmentId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_OwnerId_OccurredOn",
                table: "Entries",
                columns: new[] { "OwnerId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_RecurringItemId_RecurringMonth",
                table: "Entries",
                columns: new[] { "RecurringItemId", "RecurringMonth" },
                unique: true,
                filter: "[RecurringItemId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_QuoteId",
                table: "Contracts",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteItems_QuoteId_Position",
                table: "QuoteItems",
                columns: new[] { "QuoteId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_EventId",
                table: "Quotes",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_OwnerId_IssuedOn",
                table: "Quotes",
                columns: new[] { "OwnerId", "IssuedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_OwnerId_Number",
                table: "Quotes",
                columns: new[] { "OwnerId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringItems_OwnerId_Kind_IsActive",
                table: "RecurringItems",
                columns: new[] { "OwnerId", "Kind", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Quotes_QuoteId",
                table: "Contracts",
                column: "QuoteId",
                principalTable: "Quotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Users_OwnerId",
                table: "Contracts",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Entries_Events_EventId",
                table: "Entries",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Entries_RecurringItems_RecurringItemId",
                table: "Entries",
                column: "RecurringItemId",
                principalTable: "RecurringItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Entries_Users_OwnerId",
                table: "Entries",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Quotes_QuoteId",
                table: "Contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Users_OwnerId",
                table: "Contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_Entries_Events_EventId",
                table: "Entries");

            migrationBuilder.DropForeignKey(
                name: "FK_Entries_RecurringItems_RecurringItemId",
                table: "Entries");

            migrationBuilder.DropForeignKey(
                name: "FK_Entries_Users_OwnerId",
                table: "Entries");

            migrationBuilder.DropTable(
                name: "QuoteItems");

            migrationBuilder.DropTable(
                name: "RecurringItems");

            migrationBuilder.DropTable(
                name: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_Entries_InstallmentId",
                table: "Entries");

            migrationBuilder.DropIndex(
                name: "IX_Entries_OwnerId_OccurredOn",
                table: "Entries");

            migrationBuilder.DropIndex(
                name: "IX_Entries_RecurringItemId_RecurringMonth",
                table: "Entries");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_QuoteId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "RecurringItemId",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "RecurringMonth",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                table: "Contracts");

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "Entries",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "Contracts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Entries_Events_EventId",
                table: "Entries",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
