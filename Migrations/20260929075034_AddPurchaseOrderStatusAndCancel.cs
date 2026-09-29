using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderStatusAndCancel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "PurchaseOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "PurchaseOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "PurchaseOrders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Completed");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "PurchaseOrders",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "PurchaseOrders");
        }
    }
}
