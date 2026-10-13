using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhoneStore.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptionalCustomerMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "NewTierId",
                table: "CustomerTierHistories",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "TierId",
                table: "CustomerProfiles",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM CustomerProfiles WHERE TierId IS NULL) OR EXISTS (SELECT 1 FROM CustomerTierHistories WHERE NewTierId IS NULL) THROW 51092, 'Cannot downgrade unranked membership without losing history.', 1;");
            migrationBuilder.AlterColumn<long>(
                name: "NewTierId",
                table: "CustomerTierHistories",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "TierId",
                table: "CustomerProfiles",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
