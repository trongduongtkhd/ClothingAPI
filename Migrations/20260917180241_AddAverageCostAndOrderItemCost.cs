using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddAverageCostAndOrderItemCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LatestCostPrice",
                table: "ProductVariants",
                newName: "AverageCostPrice");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "OrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "OrderItems");

            migrationBuilder.RenameColumn(
                name: "AverageCostPrice",
                table: "ProductVariants",
                newName: "LatestCostPrice");
        }
    }
}
