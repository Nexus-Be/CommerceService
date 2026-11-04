using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

public partial class FixSubOrderJsonSeed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The historical migration owns the demo rows. Only normalize its legacy JSON shape.
        migrationBuilder.Sql("""
            UPDATE "SubOrders"
            SET "ShippingAddress" = jsonb_build_object(
                'ReceiverName', COALESCE("ShippingAddress"->>'fullName', 'John Buyer'),
                'ContactPhone', COALESCE("ShippingAddress"->>'phone', '0900000004'),
                'Street', COALESCE("ShippingAddress"->>'Street', '123 Nguyen Trai'),
                'Ward', COALESCE("ShippingAddress"->>'Ward', 'Thanh Xuan'),
                'District', COALESCE("ShippingAddress"->>'District', 'Thanh Xuan'),
                'Province', COALESCE("ShippingAddress"->>'Province', 'Ha Noi'),
                'GhnDistrictId', COALESCE(("ShippingAddress"->>'GhnDistrictId')::integer, 1442),
                'GhnWardCode', COALESCE("ShippingAddress"->>'GhnWardCode', '20101'),
                'FullAddressText', COALESCE("ShippingAddress"->>'address', '123 Nguyen Trai, Thanh Xuan, Ha Noi')
            )
            WHERE "Id" = '02000000-0000-0000-0000-000000000001'
              AND "ShippingAddress" ? 'fullName';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep demo data and the corrected address when rolling back this metadata migration.
    }
}
