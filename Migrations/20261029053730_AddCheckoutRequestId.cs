using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_RequestId",
                table: "Orders",
                columns: new[] { "UserId", "RequestId" },
                unique: true,
                filter: "[RequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId_RequestId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Orders");
        }
    }
}
