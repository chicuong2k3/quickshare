using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickShare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadSessionFileRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FileRequestId",
                table: "upload_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_FileRequestId",
                table: "upload_sessions",
                column: "FileRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_upload_sessions_file_requests_FileRequestId",
                table: "upload_sessions",
                column: "FileRequestId",
                principalTable: "file_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_upload_sessions_file_requests_FileRequestId",
                table: "upload_sessions");

            migrationBuilder.DropIndex(
                name: "IX_upload_sessions_FileRequestId",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "FileRequestId",
                table: "upload_sessions");
        }
    }
}
