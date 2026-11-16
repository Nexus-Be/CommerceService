using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentTrackingToSubOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CarrierCode",
                table: "SubOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingCode",
                table: "SubOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelPrintUrl",
                table: "SubOrders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EstimatedDeliveryAt",
                table: "SubOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShipmentPendingCreation",
                table: "SubOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_SubOrders_TrackingCode",
                table: "SubOrders",
                column: "TrackingCode");

            migrationBuilder.AddColumn<string>(
                name: "CarrierCode",
                table: "AuctionOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingCode",
                table: "AuctionOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelPrintUrl",
                table: "AuctionOrders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EstimatedDeliveryAt",
                table: "AuctionOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionOrders_TrackingCode",
                table: "AuctionOrders",
                column: "TrackingCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubOrders_TrackingCode",
                table: "SubOrders");

            migrationBuilder.DropColumn(
                name: "CarrierCode",
                table: "SubOrders");

            migrationBuilder.DropColumn(
                name: "TrackingCode",
                table: "SubOrders");

            migrationBuilder.DropColumn(
                name: "LabelPrintUrl",
                table: "SubOrders");

            migrationBuilder.DropColumn(
                name: "EstimatedDeliveryAt",
                table: "SubOrders");

            migrationBuilder.DropColumn(
                name: "ShipmentPendingCreation",
                table: "SubOrders");

            migrationBuilder.DropIndex(
                name: "IX_AuctionOrders_TrackingCode",
                table: "AuctionOrders");

            migrationBuilder.DropColumn(
                name: "CarrierCode",
                table: "AuctionOrders");

            migrationBuilder.DropColumn(
                name: "TrackingCode",
                table: "AuctionOrders");

            migrationBuilder.DropColumn(
                name: "LabelPrintUrl",
                table: "AuctionOrders");

            migrationBuilder.DropColumn(
                name: "EstimatedDeliveryAt",
                table: "AuctionOrders");
        }
    }
}
