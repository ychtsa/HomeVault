using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeVault.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailAndPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "ResidentUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetTokenExpiresAt",
                table: "ResidentUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "ResidentUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidentUsers_Email",
                table: "ResidentUsers",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResidentUsers_Email",
                table: "ResidentUsers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "ResidentUsers");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenExpiresAt",
                table: "ResidentUsers");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "ResidentUsers");
        }
    }
}
