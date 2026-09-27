using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDefaultAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultAdmin",
                table: "TenantRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // このマイグレーション以前にテナントロールを作成できたのは、テナント作成時に既定ロールを作る運営者APIだけのため、
            // 既定の名前で既存テナントの既定ロールを特定して印を付ける(以後は名前ではなく IsDefaultAdmin で判定する)。
            migrationBuilder.Sql("UPDATE [TenantRoles] SET [IsDefaultAdmin] = 1 WHERE [Name] = N'テナント管理者';");

            migrationBuilder.CreateIndex(
                name: "IX_TenantRoles_TenantId",
                table: "TenantRoles",
                column: "TenantId",
                unique: true,
                filter: "[IsDefaultAdmin] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantRoles_TenantId",
                table: "TenantRoles");

            migrationBuilder.DropColumn(
                name: "IsDefaultAdmin",
                table: "TenantRoles");
        }
    }
}
