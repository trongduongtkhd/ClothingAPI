using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingAPI.Migrations
{
    /// <inheritdoc />
    public partial class MakeVariantPriceNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "ProductVariants",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            // Biến thể có giá trùng hoàn toàn với sản phẩm thì chuyển sang kế thừa giá sản phẩm.
            migrationBuilder.Sql(@"
UPDATE v
SET v.Price = NULL, v.SalePrice = NULL
FROM ProductVariants v
INNER JOIN Products p ON p.ProductId = v.ProductId
WHERE v.Price = p.BasePrice
  AND (v.SalePrice = p.SalePrice OR (v.SalePrice IS NULL AND p.SalePrice IS NULL));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Điền lại giá từ sản phẩm cho các biến thể đang kế thừa trước khi bắt buộc NOT NULL.
            migrationBuilder.Sql(@"
UPDATE v
SET v.Price = p.BasePrice, v.SalePrice = p.SalePrice
FROM ProductVariants v
INNER JOIN Products p ON p.ProductId = v.ProductId
WHERE v.Price IS NULL;");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "ProductVariants",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
