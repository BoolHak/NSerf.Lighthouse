using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NSerf.Lighthouse.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clusters",
                columns: table => new
                {
                    cluster_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clusters", x => x.cluster_id);
                });

            migrationBuilder.CreateTable(
                name: "nodes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cluster_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    version_number = table.Column<long>(type: "bigint", nullable: false),
                    encrypted_payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    server_timestamp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nodes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clusters_cluster_id",
                table: "clusters",
                column: "cluster_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_nodes_cluster_version_timestamp",
                table: "nodes",
                columns: new[] { "cluster_id", "version_name", "version_number", "server_timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clusters");

            migrationBuilder.DropTable(
                name: "nodes");
        }
    }
}
