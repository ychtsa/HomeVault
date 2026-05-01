using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeVault.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Catalogs",
                columns: table => new
                {
                    CatalogId = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Catalogs", x => x.CatalogId);
                });

            migrationBuilder.CreateTable(
                name: "CatalogItems",
                columns: table => new
                {
                    ItemId = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    CatalogId = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ItemValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItems", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_CatalogItems_Catalogs_CatalogId",
                        column: x => x.CatalogId,
                        principalTable: "Catalogs",
                        principalColumn: "CatalogId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Residents",
                columns: table => new
                {
                    ResidentId = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    ResidentName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ResidentAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CatalogId = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Residents", x => x.ResidentId);
                    table.ForeignKey(
                        name: "FK_Residents_Catalogs_CatalogId",
                        column: x => x.CatalogId,
                        principalTable: "Catalogs",
                        principalColumn: "CatalogId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResidentUsers",
                columns: table => new
                {
                    ResidentId = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentUsers", x => x.ResidentId);
                    table.ForeignKey(
                        name: "FK_ResidentUsers_Residents_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Residents",
                        principalColumn: "ResidentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_CatalogId",
                table: "CatalogItems",
                column: "CatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_Residents_CatalogId",
                table: "Residents",
                column: "CatalogId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidentUsers_Username",
                table: "ResidentUsers",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogItems");

            migrationBuilder.DropTable(
                name: "ResidentUsers");

            migrationBuilder.DropTable(
                name: "Residents");

            migrationBuilder.DropTable(
                name: "Catalogs");
        }
    }
}
