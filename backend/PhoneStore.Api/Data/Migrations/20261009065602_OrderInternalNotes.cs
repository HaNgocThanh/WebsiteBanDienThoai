using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhoneStore.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrderInternalNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderInternalNotes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderInternalNotes", x => x.Id);
                    table.CheckConstraint("CK_OrderInternalNotes_Text", "LEN(LTRIM(RTRIM([Text]))) > 0");
                    table.ForeignKey(
                        name: "FK_OrderInternalNotes_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderInternalNotes_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInternalNotes_ActorUserId",
                table: "OrderInternalNotes",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInternalNotes_OrderId_ActorUserId_OperationKey",
                table: "OrderInternalNotes",
                columns: new[] { "OrderId", "ActorUserId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderInternalNotes_OrderId_CreatedAt",
                table: "OrderInternalNotes",
                columns: new[] { "OrderId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderInternalNotes");
        }
    }
}
