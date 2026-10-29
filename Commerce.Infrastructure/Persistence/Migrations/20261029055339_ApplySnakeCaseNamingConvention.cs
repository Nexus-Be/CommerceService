using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplySnakeCaseNamingConvention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_Carts_CartId",
                table: "CartItems");

            migrationBuilder.DropForeignKey(
                name: "FK_DiscountCodeScopes_DiscountCodes_DiscountCodeId",
                table: "DiscountCodeScopes");

            migrationBuilder.DropForeignKey(
                name: "FK_DiscountCodeUsages_DiscountCodes_DiscountCodeId",
                table: "DiscountCodeUsages");

            migrationBuilder.DropForeignKey(
                name: "FK_DiscountCodeUsages_Orders_OrderId",
                table: "DiscountCodeUsages");

            migrationBuilder.DropForeignKey(
                name: "FK_DiscountCodeUsages_SubOrders_SubOrderId",
                table: "DiscountCodeUsages");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderCancellations_AuctionOrders_AuctionOrderId",
                table: "OrderCancellations");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderCancellations_SubOrders_SubOrderId",
                table: "OrderCancellations");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderInvoices_AuctionOrders_AuctionOrderId",
                table: "OrderInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderInvoices_Orders_OrderId",
                table: "OrderInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderInvoices_SubOrders_SubOrderId",
                table: "OrderInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderRefunds_AuctionOrders_AuctionOrderId",
                table: "OrderRefunds");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderRefunds_OrderCancellations_CancellationId",
                table: "OrderRefunds");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderRefunds_Orders_OrderId",
                table: "OrderRefunds");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderRefunds_SubOrders_SubOrderId",
                table: "OrderRefunds");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Carts_CartId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderStatusHistories_AuctionOrders_AuctionOrderId",
                table: "OrderStatusHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderStatusHistories_SubOrders_SubOrderId",
                table: "OrderStatusHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                table: "OutboxMessage");

            migrationBuilder.DropForeignKey(
                name: "FK_OutboxMessage_OutboxState_OutboxId",
                table: "OutboxMessage");

            migrationBuilder.DropForeignKey(
                name: "FK_SubOrderItems_SubOrders_SubOrderId",
                table: "SubOrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SubOrders_Orders_OrderId",
                table: "SubOrders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Orders",
                table: "Orders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Carts",
                table: "Carts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SystemSettings",
                table: "SystemSettings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubOrders",
                table: "SubOrders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubOrderItems",
                table: "SubOrderItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OutboxState",
                table: "OutboxState");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OutboxMessage",
                table: "OutboxMessage");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderStatusHistories",
                table: "OrderStatusHistories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderRefunds",
                table: "OrderRefunds");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderInvoices",
                table: "OrderInvoices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrderCancellations",
                table: "OrderCancellations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InboxState_MessageId_ConsumerId",
                table: "InboxState");

            migrationBuilder.DropPrimaryKey(
                name: "PK_InboxState",
                table: "InboxState");

            migrationBuilder.DropPrimaryKey(
                name: "PK_IdempotencyKeys",
                table: "IdempotencyKeys");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DiscountCodeUsages",
                table: "DiscountCodeUsages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DiscountCodeScopes",
                table: "DiscountCodeScopes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DiscountCodes",
                table: "DiscountCodes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DailyOrderSequences",
                table: "DailyOrderSequences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CartItems",
                table: "CartItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AuditLogs",
                table: "AuditLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AuctionOrders",
                table: "AuctionOrders");

            migrationBuilder.RenameTable(
                name: "Orders",
                newName: "orders");

            migrationBuilder.RenameTable(
                name: "Carts",
                newName: "carts");

            migrationBuilder.RenameTable(
                name: "SystemSettings",
                newName: "system_settings");

            migrationBuilder.RenameTable(
                name: "SubOrders",
                newName: "sub_orders");

            migrationBuilder.RenameTable(
                name: "SubOrderItems",
                newName: "sub_order_items");

            migrationBuilder.RenameTable(
                name: "OutboxState",
                newName: "outbox_state");

            migrationBuilder.RenameTable(
                name: "OutboxMessage",
                newName: "outbox_message");

            migrationBuilder.RenameTable(
                name: "OrderStatusHistories",
                newName: "order_status_histories");

            migrationBuilder.RenameTable(
                name: "OrderRefunds",
                newName: "order_refunds");

            migrationBuilder.RenameTable(
                name: "OrderInvoices",
                newName: "order_invoices");

            migrationBuilder.RenameTable(
                name: "OrderCancellations",
                newName: "order_cancellations");

            migrationBuilder.RenameTable(
                name: "InboxState",
                newName: "inbox_state");

            migrationBuilder.RenameTable(
                name: "IdempotencyKeys",
                newName: "idempotency_keys");

            migrationBuilder.RenameTable(
                name: "DiscountCodeUsages",
                newName: "discount_code_usages");

            migrationBuilder.RenameTable(
                name: "DiscountCodeScopes",
                newName: "discount_code_scopes");

            migrationBuilder.RenameTable(
                name: "DiscountCodes",
                newName: "discount_codes");

            migrationBuilder.RenameTable(
                name: "DailyOrderSequences",
                newName: "daily_order_sequences");

            migrationBuilder.RenameTable(
                name: "CartItems",
                newName: "cart_items");

            migrationBuilder.RenameTable(
                name: "AuditLogs",
                newName: "audit_logs");

            migrationBuilder.RenameTable(
                name: "AuctionOrders",
                newName: "auction_orders");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "orders",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "orders",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "WalletTransactionId",
                table: "orders",
                newName: "wallet_transaction_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "orders",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TotalAmount",
                table: "orders",
                newName: "total_amount");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "orders",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "PaymentDeadlineAt",
                table: "orders",
                newName: "payment_deadline_at");

            migrationBuilder.RenameColumn(
                name: "PaymentAttemptCount",
                table: "orders",
                newName: "payment_attempt_count");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "orders",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "orders",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "orders",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CartId",
                table: "orders",
                newName: "cart_id");

            migrationBuilder.RenameColumn(
                name: "BuyerId",
                table: "orders",
                newName: "buyer_id");

            migrationBuilder.RenameIndex(
                name: "IX_Orders_CartId",
                table: "orders",
                newName: "ix_orders_cart_id");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "carts",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "carts",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "carts",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SubtotalAmount",
                table: "carts",
                newName: "subtotal_amount");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "carts",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "LastActivityAt",
                table: "carts",
                newName: "last_activity_at");

            migrationBuilder.RenameColumn(
                name: "ItemCount",
                table: "carts",
                newName: "item_count");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "carts",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "carts",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "carts",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "BuyerId",
                table: "carts",
                newName: "buyer_id");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "system_settings",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "system_settings",
                newName: "category");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "system_settings",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "ValueType",
                table: "system_settings",
                newName: "value_type");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "system_settings",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "system_settings",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SettingValue",
                table: "system_settings",
                newName: "setting_value");

            migrationBuilder.RenameColumn(
                name: "SettingKey",
                table: "system_settings",
                newName: "setting_key");

            migrationBuilder.RenameColumn(
                name: "IsEditable",
                table: "system_settings",
                newName: "is_editable");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "system_settings",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "system_settings",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "system_settings",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_SystemSettings_SettingKey",
                table: "system_settings",
                newName: "ix_system_settings_setting_key");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "sub_orders",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "sub_orders",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "WalletTransactionId",
                table: "sub_orders",
                newName: "wallet_transaction_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "sub_orders",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TotalAmount",
                table: "sub_orders",
                newName: "total_amount");

            migrationBuilder.RenameColumn(
                name: "SystemDiscountAmount",
                table: "sub_orders",
                newName: "system_discount_amount");

            migrationBuilder.RenameColumn(
                name: "SubOrderNo",
                table: "sub_orders",
                newName: "sub_order_no");

            migrationBuilder.RenameColumn(
                name: "ShopDiscountAmount",
                table: "sub_orders",
                newName: "shop_discount_amount");

            migrationBuilder.RenameColumn(
                name: "ShippingFeeAmount",
                table: "sub_orders",
                newName: "shipping_fee_amount");

            migrationBuilder.RenameColumn(
                name: "ShippingAddress",
                table: "sub_orders",
                newName: "shipping_address");

            migrationBuilder.RenameColumn(
                name: "ShippedAt",
                table: "sub_orders",
                newName: "shipped_at");

            migrationBuilder.RenameColumn(
                name: "SellerRespondDeadlineAt",
                table: "sub_orders",
                newName: "seller_respond_deadline_at");

            migrationBuilder.RenameColumn(
                name: "SellerReceiveAmount",
                table: "sub_orders",
                newName: "seller_receive_amount");

            migrationBuilder.RenameColumn(
                name: "SellerId",
                table: "sub_orders",
                newName: "seller_id");

            migrationBuilder.RenameColumn(
                name: "SellerAcceptedAt",
                table: "sub_orders",
                newName: "seller_accepted_at");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "sub_orders",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "ProductDiscountAmount",
                table: "sub_orders",
                newName: "product_discount_amount");

            migrationBuilder.RenameColumn(
                name: "PlatformMarkupAmount",
                table: "sub_orders",
                newName: "platform_markup_amount");

            migrationBuilder.RenameColumn(
                name: "PlatformFeeAmount",
                table: "sub_orders",
                newName: "platform_fee_amount");

            migrationBuilder.RenameColumn(
                name: "PaymentMethod",
                table: "sub_orders",
                newName: "payment_method");

            migrationBuilder.RenameColumn(
                name: "PaymentDeadlineAt",
                table: "sub_orders",
                newName: "payment_deadline_at");

            migrationBuilder.RenameColumn(
                name: "PaidAt",
                table: "sub_orders",
                newName: "paid_at");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                table: "sub_orders",
                newName: "order_id");

            migrationBuilder.RenameColumn(
                name: "ItemsAmount",
                table: "sub_orders",
                newName: "items_amount");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "sub_orders",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "DeliveredAt",
                table: "sub_orders",
                newName: "delivered_at");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "sub_orders",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "sub_orders",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ContactPhone",
                table: "sub_orders",
                newName: "contact_phone");

            migrationBuilder.RenameColumn(
                name: "CompletedAt",
                table: "sub_orders",
                newName: "completed_at");

            migrationBuilder.RenameColumn(
                name: "CancelledAt",
                table: "sub_orders",
                newName: "cancelled_at");

            migrationBuilder.RenameColumn(
                name: "BuyerId",
                table: "sub_orders",
                newName: "buyer_id");

            migrationBuilder.RenameIndex(
                name: "IX_SubOrders_SubOrderNo",
                table: "sub_orders",
                newName: "ix_sub_orders_sub_order_no");

            migrationBuilder.RenameIndex(
                name: "IX_SubOrders_OrderId",
                table: "sub_orders",
                newName: "ix_sub_orders_order_id");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "sub_order_items",
                newName: "quantity");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "sub_order_items",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "sub_order_items",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "UnitPriceAfterDiscount",
                table: "sub_order_items",
                newName: "unit_price_after_discount");

            migrationBuilder.RenameColumn(
                name: "UnitPrice",
                table: "sub_order_items",
                newName: "unit_price");

            migrationBuilder.RenameColumn(
                name: "SubOrderId",
                table: "sub_order_items",
                newName: "sub_order_id");

            migrationBuilder.RenameColumn(
                name: "SkuId",
                table: "sub_order_items",
                newName: "sku_id");

            migrationBuilder.RenameColumn(
                name: "SkuCodeSnapshot",
                table: "sub_order_items",
                newName: "sku_code_snapshot");

            migrationBuilder.RenameColumn(
                name: "SkuAttributesSnapshot",
                table: "sub_order_items",
                newName: "sku_attributes_snapshot");

            migrationBuilder.RenameColumn(
                name: "SellerId",
                table: "sub_order_items",
                newName: "seller_id");

            migrationBuilder.RenameColumn(
                name: "ProductNameSnapshot",
                table: "sub_order_items",
                newName: "product_name_snapshot");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "sub_order_items",
                newName: "product_id");

            migrationBuilder.RenameColumn(
                name: "LineTotal",
                table: "sub_order_items",
                newName: "line_total");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "sub_order_items",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "ImageUrlSnapshot",
                table: "sub_order_items",
                newName: "image_url_snapshot");

            migrationBuilder.RenameColumn(
                name: "DiscountCodeId",
                table: "sub_order_items",
                newName: "discount_code_id");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "sub_order_items",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "sub_order_items",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_SubOrderItems_SubOrderId",
                table: "sub_order_items",
                newName: "ix_sub_order_items_sub_order_id");

            migrationBuilder.RenameColumn(
                name: "Delivered",
                table: "outbox_state",
                newName: "delivered");

            migrationBuilder.RenameColumn(
                name: "Created",
                table: "outbox_state",
                newName: "created");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "outbox_state",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "LockId",
                table: "outbox_state",
                newName: "lock_id");

            migrationBuilder.RenameColumn(
                name: "LastSequenceNumber",
                table: "outbox_state",
                newName: "last_sequence_number");

            migrationBuilder.RenameColumn(
                name: "OutboxId",
                table: "outbox_state",
                newName: "outbox_id");

            migrationBuilder.RenameIndex(
                name: "IX_OutboxState_Created",
                table: "outbox_state",
                newName: "ix_outbox_state_created");

            migrationBuilder.RenameColumn(
                name: "Properties",
                table: "outbox_message",
                newName: "properties");

            migrationBuilder.RenameColumn(
                name: "Headers",
                table: "outbox_message",
                newName: "headers");

            migrationBuilder.RenameColumn(
                name: "Body",
                table: "outbox_message",
                newName: "body");

            migrationBuilder.RenameColumn(
                name: "SourceAddress",
                table: "outbox_message",
                newName: "source_address");

            migrationBuilder.RenameColumn(
                name: "SentTime",
                table: "outbox_message",
                newName: "sent_time");

            migrationBuilder.RenameColumn(
                name: "ResponseAddress",
                table: "outbox_message",
                newName: "response_address");

            migrationBuilder.RenameColumn(
                name: "RequestId",
                table: "outbox_message",
                newName: "request_id");

            migrationBuilder.RenameColumn(
                name: "OutboxId",
                table: "outbox_message",
                newName: "outbox_id");

            migrationBuilder.RenameColumn(
                name: "MessageType",
                table: "outbox_message",
                newName: "message_type");

            migrationBuilder.RenameColumn(
                name: "MessageId",
                table: "outbox_message",
                newName: "message_id");

            migrationBuilder.RenameColumn(
                name: "InitiatorId",
                table: "outbox_message",
                newName: "initiator_id");

            migrationBuilder.RenameColumn(
                name: "InboxMessageId",
                table: "outbox_message",
                newName: "inbox_message_id");

            migrationBuilder.RenameColumn(
                name: "InboxConsumerId",
                table: "outbox_message",
                newName: "inbox_consumer_id");

            migrationBuilder.RenameColumn(
                name: "FaultAddress",
                table: "outbox_message",
                newName: "fault_address");

            migrationBuilder.RenameColumn(
                name: "ExpirationTime",
                table: "outbox_message",
                newName: "expiration_time");

            migrationBuilder.RenameColumn(
                name: "EnqueueTime",
                table: "outbox_message",
                newName: "enqueue_time");

            migrationBuilder.RenameColumn(
                name: "DestinationAddress",
                table: "outbox_message",
                newName: "destination_address");

            migrationBuilder.RenameColumn(
                name: "CorrelationId",
                table: "outbox_message",
                newName: "correlation_id");

            migrationBuilder.RenameColumn(
                name: "ConversationId",
                table: "outbox_message",
                newName: "conversation_id");

            migrationBuilder.RenameColumn(
                name: "ContentType",
                table: "outbox_message",
                newName: "content_type");

            migrationBuilder.RenameColumn(
                name: "SequenceNumber",
                table: "outbox_message",
                newName: "sequence_number");

            migrationBuilder.RenameIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                table: "outbox_message",
                newName: "ix_outbox_message_outbox_id_sequence_number");

            migrationBuilder.RenameIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                table: "outbox_message",
                newName: "ix_outbox_message_inbox_message_id_inbox_consumer_id_sequence_");

            migrationBuilder.RenameIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                table: "outbox_message",
                newName: "ix_outbox_message_expiration_time");

            migrationBuilder.RenameIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                table: "outbox_message",
                newName: "ix_outbox_message_enqueue_time");

            migrationBuilder.RenameColumn(
                name: "Reason",
                table: "order_status_histories",
                newName: "reason");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "order_status_histories",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "order_status_histories",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "ToStatus",
                table: "order_status_histories",
                newName: "to_status");

            migrationBuilder.RenameColumn(
                name: "SubOrderId",
                table: "order_status_histories",
                newName: "sub_order_id");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "order_status_histories",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "FromStatus",
                table: "order_status_histories",
                newName: "from_status");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "order_status_histories",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "order_status_histories",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ChangedBy",
                table: "order_status_histories",
                newName: "changed_by");

            migrationBuilder.RenameColumn(
                name: "AuctionOrderId",
                table: "order_status_histories",
                newName: "auction_order_id");

            migrationBuilder.RenameColumn(
                name: "ActorRole",
                table: "order_status_histories",
                newName: "actor_role");

            migrationBuilder.RenameIndex(
                name: "IX_OrderStatusHistories_SubOrderId",
                table: "order_status_histories",
                newName: "ix_order_status_histories_sub_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderStatusHistories_AuctionOrderId",
                table: "order_status_histories",
                newName: "ix_order_status_histories_auction_order_id");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "order_refunds",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "order_refunds",
                newName: "amount");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "order_refunds",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "WalletTransactionId",
                table: "order_refunds",
                newName: "wallet_transaction_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "order_refunds",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SubOrderId",
                table: "order_refunds",
                newName: "sub_order_id");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "order_refunds",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "RequestedBy",
                table: "order_refunds",
                newName: "requested_by");

            migrationBuilder.RenameColumn(
                name: "RequestedAt",
                table: "order_refunds",
                newName: "requested_at");

            migrationBuilder.RenameColumn(
                name: "RequestEventId",
                table: "order_refunds",
                newName: "request_event_id");

            migrationBuilder.RenameColumn(
                name: "RefundType",
                table: "order_refunds",
                newName: "refund_type");

            migrationBuilder.RenameColumn(
                name: "RefundTransactionId",
                table: "order_refunds",
                newName: "refund_transaction_id");

            migrationBuilder.RenameColumn(
                name: "ReasonNote",
                table: "order_refunds",
                newName: "reason_note");

            migrationBuilder.RenameColumn(
                name: "ReasonCode",
                table: "order_refunds",
                newName: "reason_code");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                table: "order_refunds",
                newName: "order_id");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "order_refunds",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "FailureCode",
                table: "order_refunds",
                newName: "failure_code");

            migrationBuilder.RenameColumn(
                name: "FailedAt",
                table: "order_refunds",
                newName: "failed_at");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "order_refunds",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "order_refunds",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CompletedAt",
                table: "order_refunds",
                newName: "completed_at");

            migrationBuilder.RenameColumn(
                name: "CancellationId",
                table: "order_refunds",
                newName: "cancellation_id");

            migrationBuilder.RenameColumn(
                name: "AuctionOrderId",
                table: "order_refunds",
                newName: "auction_order_id");

            migrationBuilder.RenameColumn(
                name: "ActorRole",
                table: "order_refunds",
                newName: "actor_role");

            migrationBuilder.RenameIndex(
                name: "IX_OrderRefunds_SubOrderId",
                table: "order_refunds",
                newName: "ix_order_refunds_sub_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderRefunds_OrderId",
                table: "order_refunds",
                newName: "ix_order_refunds_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderRefunds_CancellationId",
                table: "order_refunds",
                newName: "ix_order_refunds_cancellation_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderRefunds_AuctionOrderId",
                table: "order_refunds",
                newName: "ix_order_refunds_auction_order_id");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "order_invoices",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "order_invoices",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "order_invoices",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TotalAmount",
                table: "order_invoices",
                newName: "total_amount");

            migrationBuilder.RenameColumn(
                name: "SubOrderId",
                table: "order_invoices",
                newName: "sub_order_id");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                table: "order_invoices",
                newName: "order_id");

            migrationBuilder.RenameColumn(
                name: "IssuedAt",
                table: "order_invoices",
                newName: "issued_at");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "order_invoices",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "InvoiceNo",
                table: "order_invoices",
                newName: "invoice_no");

            migrationBuilder.RenameColumn(
                name: "FileUrl",
                table: "order_invoices",
                newName: "file_url");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "order_invoices",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "order_invoices",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "AuctionOrderId",
                table: "order_invoices",
                newName: "auction_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderInvoices_SubOrderId",
                table: "order_invoices",
                newName: "ix_order_invoices_sub_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderInvoices_OrderId",
                table: "order_invoices",
                newName: "ix_order_invoices_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderInvoices_InvoiceNo",
                table: "order_invoices",
                newName: "ix_order_invoices_invoice_no");

            migrationBuilder.RenameIndex(
                name: "IX_OrderInvoices_AuctionOrderId",
                table: "order_invoices",
                newName: "ix_order_invoices_auction_order_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "order_cancellations",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "WasPaid",
                table: "order_cancellations",
                newName: "was_paid");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "order_cancellations",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SubOrderId",
                table: "order_cancellations",
                newName: "sub_order_id");

            migrationBuilder.RenameColumn(
                name: "RefundTransactionId",
                table: "order_cancellations",
                newName: "refund_transaction_id");

            migrationBuilder.RenameColumn(
                name: "ReasonNote",
                table: "order_cancellations",
                newName: "reason_note");

            migrationBuilder.RenameColumn(
                name: "ReasonCode",
                table: "order_cancellations",
                newName: "reason_code");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "order_cancellations",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "InventoryReleased",
                table: "order_cancellations",
                newName: "inventory_released");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "order_cancellations",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "order_cancellations",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CancelledBy",
                table: "order_cancellations",
                newName: "cancelled_by");

            migrationBuilder.RenameColumn(
                name: "AuctionOrderId",
                table: "order_cancellations",
                newName: "auction_order_id");

            migrationBuilder.RenameColumn(
                name: "ActorRole",
                table: "order_cancellations",
                newName: "actor_role");

            migrationBuilder.RenameIndex(
                name: "IX_OrderCancellations_SubOrderId",
                table: "order_cancellations",
                newName: "ix_order_cancellations_sub_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_OrderCancellations_AuctionOrderId",
                table: "order_cancellations",
                newName: "ix_order_cancellations_auction_order_id");

            migrationBuilder.RenameColumn(
                name: "Received",
                table: "inbox_state",
                newName: "received");

            migrationBuilder.RenameColumn(
                name: "Delivered",
                table: "inbox_state",
                newName: "delivered");

            migrationBuilder.RenameColumn(
                name: "Consumed",
                table: "inbox_state",
                newName: "consumed");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "inbox_state",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "inbox_state",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "ReceiveCount",
                table: "inbox_state",
                newName: "receive_count");

            migrationBuilder.RenameColumn(
                name: "MessageId",
                table: "inbox_state",
                newName: "message_id");

            migrationBuilder.RenameColumn(
                name: "LockId",
                table: "inbox_state",
                newName: "lock_id");

            migrationBuilder.RenameColumn(
                name: "LastSequenceNumber",
                table: "inbox_state",
                newName: "last_sequence_number");

            migrationBuilder.RenameColumn(
                name: "ExpirationTime",
                table: "inbox_state",
                newName: "expiration_time");

            migrationBuilder.RenameColumn(
                name: "ConsumerId",
                table: "inbox_state",
                newName: "consumer_id");

            migrationBuilder.RenameIndex(
                name: "IX_InboxState_Delivered",
                table: "inbox_state",
                newName: "ix_inbox_state_delivered");

            migrationBuilder.RenameColumn(
                name: "Scope",
                table: "idempotency_keys",
                newName: "scope");

            migrationBuilder.RenameColumn(
                name: "Key",
                table: "idempotency_keys",
                newName: "key");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "idempotency_keys",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "idempotency_keys",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "ResponseStatusCode",
                table: "idempotency_keys",
                newName: "response_status_code");

            migrationBuilder.RenameColumn(
                name: "ResponseBody",
                table: "idempotency_keys",
                newName: "response_body");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "idempotency_keys",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "idempotency_keys",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "idempotency_keys",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "idempotency_keys",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_IdempotencyKeys_Key_Scope",
                table: "idempotency_keys",
                newName: "ix_idempotency_keys_key_scope");

            migrationBuilder.RenameIndex(
                name: "IX_IdempotencyKeys_ExpiresAt",
                table: "idempotency_keys",
                newName: "ix_idempotency_keys_expires_at");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "discount_code_usages",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "discount_code_usages",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "discount_code_usages",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UsedAt",
                table: "discount_code_usages",
                newName: "used_at");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "discount_code_usages",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SubOrderId",
                table: "discount_code_usages",
                newName: "sub_order_id");

            migrationBuilder.RenameColumn(
                name: "ReservedAt",
                table: "discount_code_usages",
                newName: "reserved_at");

            migrationBuilder.RenameColumn(
                name: "ReleasedAt",
                table: "discount_code_usages",
                newName: "released_at");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                table: "discount_code_usages",
                newName: "order_id");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "discount_code_usages",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "DiscountCodeId",
                table: "discount_code_usages",
                newName: "discount_code_id");

            migrationBuilder.RenameColumn(
                name: "DiscountAmount",
                table: "discount_code_usages",
                newName: "discount_amount");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "discount_code_usages",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "discount_code_usages",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_DiscountCodeUsages_SubOrderId",
                table: "discount_code_usages",
                newName: "ix_discount_code_usages_sub_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_DiscountCodeUsages_OrderId",
                table: "discount_code_usages",
                newName: "ix_discount_code_usages_order_id");

            migrationBuilder.RenameIndex(
                name: "IX_DiscountCodeUsages_DiscountCodeId",
                table: "discount_code_usages",
                newName: "ix_discount_code_usages_discount_code_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "discount_code_scopes",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "discount_code_scopes",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "ScopeRefType",
                table: "discount_code_scopes",
                newName: "scope_ref_type");

            migrationBuilder.RenameColumn(
                name: "ScopeRefId",
                table: "discount_code_scopes",
                newName: "scope_ref_id");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "discount_code_scopes",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "DiscountCodeId",
                table: "discount_code_scopes",
                newName: "discount_code_id");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "discount_code_scopes",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "discount_code_scopes",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_DiscountCodeScopes_DiscountCodeId",
                table: "discount_code_scopes",
                newName: "ix_discount_code_scopes_discount_code_id");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "discount_codes",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "discount_codes",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "discount_codes",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "discount_codes",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UsedCount",
                table: "discount_codes",
                newName: "used_count");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "discount_codes",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "StartsAt",
                table: "discount_codes",
                newName: "starts_at");

            migrationBuilder.RenameColumn(
                name: "ScopeType",
                table: "discount_codes",
                newName: "scope_type");

            migrationBuilder.RenameColumn(
                name: "ReservedCount",
                table: "discount_codes",
                newName: "reserved_count");

            migrationBuilder.RenameColumn(
                name: "OwnerSellerId",
                table: "discount_codes",
                newName: "owner_seller_id");

            migrationBuilder.RenameColumn(
                name: "MinOrderAmount",
                table: "discount_codes",
                newName: "min_order_amount");

            migrationBuilder.RenameColumn(
                name: "MaxUsesTotal",
                table: "discount_codes",
                newName: "max_uses_total");

            migrationBuilder.RenameColumn(
                name: "MaxUsesPerUser",
                table: "discount_codes",
                newName: "max_uses_per_user");

            migrationBuilder.RenameColumn(
                name: "MaxDiscountAmount",
                table: "discount_codes",
                newName: "max_discount_amount");

            migrationBuilder.RenameColumn(
                name: "IsVisible",
                table: "discount_codes",
                newName: "is_visible");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "discount_codes",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "EndsAt",
                table: "discount_codes",
                newName: "ends_at");

            migrationBuilder.RenameColumn(
                name: "DiscountValue",
                table: "discount_codes",
                newName: "discount_value");

            migrationBuilder.RenameColumn(
                name: "DiscountType",
                table: "discount_codes",
                newName: "discount_type");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "discount_codes",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "discount_codes",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "discount_codes",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_DiscountCodes_Code",
                table: "discount_codes",
                newName: "ix_discount_codes_code");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "daily_order_sequences",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "LastValue",
                table: "daily_order_sequences",
                newName: "last_value");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "daily_order_sequences",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "SeqType",
                table: "daily_order_sequences",
                newName: "seq_type");

            migrationBuilder.RenameColumn(
                name: "SeqDate",
                table: "daily_order_sequences",
                newName: "seq_date");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "cart_items",
                newName: "quantity");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "cart_items",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "cart_items",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "UnitPriceSnapshot",
                table: "cart_items",
                newName: "unit_price_snapshot");

            migrationBuilder.RenameColumn(
                name: "SkuId",
                table: "cart_items",
                newName: "sku_id");

            migrationBuilder.RenameColumn(
                name: "SellerId",
                table: "cart_items",
                newName: "seller_id");

            migrationBuilder.RenameColumn(
                name: "ProductNameSnapshot",
                table: "cart_items",
                newName: "product_name_snapshot");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "cart_items",
                newName: "product_id");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "cart_items",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "cart_items",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "cart_items",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CartId",
                table: "cart_items",
                newName: "cart_id");

            migrationBuilder.RenameColumn(
                name: "AddedAt",
                table: "cart_items",
                newName: "added_at");

            migrationBuilder.RenameIndex(
                name: "IX_CartItems_ProductId",
                table: "cart_items",
                newName: "ix_cart_items_product_id");

            migrationBuilder.RenameIndex(
                name: "IX_CartItems_CartId_SkuId",
                table: "cart_items",
                newName: "ix_cart_items_cart_id_sku_id");

            migrationBuilder.RenameColumn(
                name: "Action",
                table: "audit_logs",
                newName: "action");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "audit_logs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserName",
                table: "audit_logs",
                newName: "user_name");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "audit_logs",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UserAgent",
                table: "audit_logs",
                newName: "user_agent");

            migrationBuilder.RenameColumn(
                name: "TraceId",
                table: "audit_logs",
                newName: "trace_id");

            migrationBuilder.RenameColumn(
                name: "ServiceName",
                table: "audit_logs",
                newName: "service_name");

            migrationBuilder.RenameColumn(
                name: "OldValues",
                table: "audit_logs",
                newName: "old_values");

            migrationBuilder.RenameColumn(
                name: "NewValues",
                table: "audit_logs",
                newName: "new_values");

            migrationBuilder.RenameColumn(
                name: "IsSynced",
                table: "audit_logs",
                newName: "is_synced");

            migrationBuilder.RenameColumn(
                name: "IpAddress",
                table: "audit_logs",
                newName: "ip_address");

            migrationBuilder.RenameColumn(
                name: "EntityName",
                table: "audit_logs",
                newName: "entity_name");

            migrationBuilder.RenameColumn(
                name: "EntityId",
                table: "audit_logs",
                newName: "entity_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "audit_logs",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ChangedColumns",
                table: "audit_logs",
                newName: "changed_columns");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "auction_orders",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "auction_orders",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "WinningBidId",
                table: "auction_orders",
                newName: "winning_bid_id");

            migrationBuilder.RenameColumn(
                name: "WinningAmount",
                table: "auction_orders",
                newName: "winning_amount");

            migrationBuilder.RenameColumn(
                name: "WalletTransactionId",
                table: "auction_orders",
                newName: "wallet_transaction_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "auction_orders",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TotalAmount",
                table: "auction_orders",
                newName: "total_amount");

            migrationBuilder.RenameColumn(
                name: "SkuId",
                table: "auction_orders",
                newName: "sku_id");

            migrationBuilder.RenameColumn(
                name: "SkuAttributesSnapshot",
                table: "auction_orders",
                newName: "sku_attributes_snapshot");

            migrationBuilder.RenameColumn(
                name: "ShippingFeeAmount",
                table: "auction_orders",
                newName: "shipping_fee_amount");

            migrationBuilder.RenameColumn(
                name: "ShippingAddress",
                table: "auction_orders",
                newName: "shipping_address");

            migrationBuilder.RenameColumn(
                name: "ShippedAt",
                table: "auction_orders",
                newName: "shipped_at");

            migrationBuilder.RenameColumn(
                name: "SellerRespondDeadlineAt",
                table: "auction_orders",
                newName: "seller_respond_deadline_at");

            migrationBuilder.RenameColumn(
                name: "SellerReceiveAmount",
                table: "auction_orders",
                newName: "seller_receive_amount");

            migrationBuilder.RenameColumn(
                name: "SellerId",
                table: "auction_orders",
                newName: "seller_id");

            migrationBuilder.RenameColumn(
                name: "SellerAcceptedAt",
                table: "auction_orders",
                newName: "seller_accepted_at");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "auction_orders",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "ProductNameSnapshot",
                table: "auction_orders",
                newName: "product_name_snapshot");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "auction_orders",
                newName: "product_id");

            migrationBuilder.RenameColumn(
                name: "PlatformFeeAmount",
                table: "auction_orders",
                newName: "platform_fee_amount");

            migrationBuilder.RenameColumn(
                name: "PaymentMethod",
                table: "auction_orders",
                newName: "payment_method");

            migrationBuilder.RenameColumn(
                name: "PaymentDeadlineAt",
                table: "auction_orders",
                newName: "payment_deadline_at");

            migrationBuilder.RenameColumn(
                name: "PaidAt",
                table: "auction_orders",
                newName: "paid_at");

            migrationBuilder.RenameColumn(
                name: "OrderNo",
                table: "auction_orders",
                newName: "order_no");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "auction_orders",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "ImageUrlSnapshot",
                table: "auction_orders",
                newName: "image_url_snapshot");

            migrationBuilder.RenameColumn(
                name: "DeliveredAt",
                table: "auction_orders",
                newName: "delivered_at");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "auction_orders",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "auction_orders",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ContactPhone",
                table: "auction_orders",
                newName: "contact_phone");

            migrationBuilder.RenameColumn(
                name: "CompletedAt",
                table: "auction_orders",
                newName: "completed_at");

            migrationBuilder.RenameColumn(
                name: "CancelledAt",
                table: "auction_orders",
                newName: "cancelled_at");

            migrationBuilder.RenameColumn(
                name: "BuyerId",
                table: "auction_orders",
                newName: "buyer_id");

            migrationBuilder.RenameColumn(
                name: "AuctionId",
                table: "auction_orders",
                newName: "auction_id");

            migrationBuilder.RenameIndex(
                name: "IX_AuctionOrders_OrderNo",
                table: "auction_orders",
                newName: "ix_auction_orders_order_no");

            migrationBuilder.AddPrimaryKey(
                name: "pk_orders",
                table: "orders",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_carts",
                table: "carts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_system_settings",
                table: "system_settings",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sub_orders",
                table: "sub_orders",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sub_order_items",
                table: "sub_order_items",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_outbox_state",
                table: "outbox_state",
                column: "outbox_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_outbox_message",
                table: "outbox_message",
                column: "sequence_number");

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_status_histories",
                table: "order_status_histories",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_refunds",
                table: "order_refunds",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_invoices",
                table: "order_invoices",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_order_cancellations",
                table: "order_cancellations",
                column: "id");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_inbox_state_message_id_consumer_id",
                table: "inbox_state",
                columns: new[] { "message_id", "consumer_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_inbox_state",
                table: "inbox_state",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_idempotency_keys",
                table: "idempotency_keys",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_discount_code_usages",
                table: "discount_code_usages",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_discount_code_scopes",
                table: "discount_code_scopes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_discount_codes",
                table: "discount_codes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_daily_order_sequences",
                table: "daily_order_sequences",
                columns: new[] { "seq_date", "seq_type" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_cart_items",
                table: "cart_items",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_audit_logs",
                table: "audit_logs",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_auction_orders",
                table: "auction_orders",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_cart_items_carts_cart_id",
                table: "cart_items",
                column: "cart_id",
                principalTable: "carts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discount_code_scopes_discount_codes_discount_code_id",
                table: "discount_code_scopes",
                column: "discount_code_id",
                principalTable: "discount_codes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discount_code_usages_discount_codes_discount_code_id",
                table: "discount_code_usages",
                column: "discount_code_id",
                principalTable: "discount_codes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discount_code_usages_orders_order_id",
                table: "discount_code_usages",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_discount_code_usages_sub_orders_sub_order_id",
                table: "discount_code_usages",
                column: "sub_order_id",
                principalTable: "sub_orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_cancellations_auction_orders_auction_order_id",
                table: "order_cancellations",
                column: "auction_order_id",
                principalTable: "auction_orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_cancellations_sub_orders_sub_order_id",
                table: "order_cancellations",
                column: "sub_order_id",
                principalTable: "sub_orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_invoices_auction_orders_auction_order_id",
                table: "order_invoices",
                column: "auction_order_id",
                principalTable: "auction_orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_invoices_orders_order_id",
                table: "order_invoices",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_order_invoices_sub_orders_sub_order_id",
                table: "order_invoices",
                column: "sub_order_id",
                principalTable: "sub_orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_refunds_auction_orders_auction_order_id",
                table: "order_refunds",
                column: "auction_order_id",
                principalTable: "auction_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_refunds_order_cancellations_cancellation_id",
                table: "order_refunds",
                column: "cancellation_id",
                principalTable: "order_cancellations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_order_refunds_orders_order_id",
                table: "order_refunds",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_refunds_sub_orders_sub_order_id",
                table: "order_refunds",
                column: "sub_order_id",
                principalTable: "sub_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_status_histories_auction_orders_auction_order_id",
                table: "order_status_histories",
                column: "auction_order_id",
                principalTable: "auction_orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_status_histories_sub_orders_sub_order_id",
                table: "order_status_histories",
                column: "sub_order_id",
                principalTable: "sub_orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_carts_cart_id",
                table: "orders",
                column: "cart_id",
                principalTable: "carts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_outbox_message_inbox_state_inbox_message_id_inbox_consumer_",
                table: "outbox_message",
                columns: new[] { "inbox_message_id", "inbox_consumer_id" },
                principalTable: "inbox_state",
                principalColumns: new[] { "message_id", "consumer_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_outbox_message_outbox_state_outbox_id",
                table: "outbox_message",
                column: "outbox_id",
                principalTable: "outbox_state",
                principalColumn: "outbox_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sub_order_items_sub_orders_sub_order_id",
                table: "sub_order_items",
                column: "sub_order_id",
                principalTable: "sub_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sub_orders_orders_order_id",
                table: "sub_orders",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cart_items_carts_cart_id",
                table: "cart_items");

            migrationBuilder.DropForeignKey(
                name: "fk_discount_code_scopes_discount_codes_discount_code_id",
                table: "discount_code_scopes");

            migrationBuilder.DropForeignKey(
                name: "fk_discount_code_usages_discount_codes_discount_code_id",
                table: "discount_code_usages");

            migrationBuilder.DropForeignKey(
                name: "fk_discount_code_usages_orders_order_id",
                table: "discount_code_usages");

            migrationBuilder.DropForeignKey(
                name: "fk_discount_code_usages_sub_orders_sub_order_id",
                table: "discount_code_usages");

            migrationBuilder.DropForeignKey(
                name: "fk_order_cancellations_auction_orders_auction_order_id",
                table: "order_cancellations");

            migrationBuilder.DropForeignKey(
                name: "fk_order_cancellations_sub_orders_sub_order_id",
                table: "order_cancellations");

            migrationBuilder.DropForeignKey(
                name: "fk_order_invoices_auction_orders_auction_order_id",
                table: "order_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_order_invoices_orders_order_id",
                table: "order_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_order_invoices_sub_orders_sub_order_id",
                table: "order_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_order_refunds_auction_orders_auction_order_id",
                table: "order_refunds");

            migrationBuilder.DropForeignKey(
                name: "fk_order_refunds_order_cancellations_cancellation_id",
                table: "order_refunds");

            migrationBuilder.DropForeignKey(
                name: "fk_order_refunds_orders_order_id",
                table: "order_refunds");

            migrationBuilder.DropForeignKey(
                name: "fk_order_refunds_sub_orders_sub_order_id",
                table: "order_refunds");

            migrationBuilder.DropForeignKey(
                name: "fk_order_status_histories_auction_orders_auction_order_id",
                table: "order_status_histories");

            migrationBuilder.DropForeignKey(
                name: "fk_order_status_histories_sub_orders_sub_order_id",
                table: "order_status_histories");

            migrationBuilder.DropForeignKey(
                name: "fk_orders_carts_cart_id",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "fk_outbox_message_inbox_state_inbox_message_id_inbox_consumer_",
                table: "outbox_message");

            migrationBuilder.DropForeignKey(
                name: "fk_outbox_message_outbox_state_outbox_id",
                table: "outbox_message");

            migrationBuilder.DropForeignKey(
                name: "fk_sub_order_items_sub_orders_sub_order_id",
                table: "sub_order_items");

            migrationBuilder.DropForeignKey(
                name: "fk_sub_orders_orders_order_id",
                table: "sub_orders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_orders",
                table: "orders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_carts",
                table: "carts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_system_settings",
                table: "system_settings");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sub_orders",
                table: "sub_orders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sub_order_items",
                table: "sub_order_items");

            migrationBuilder.DropPrimaryKey(
                name: "pk_outbox_state",
                table: "outbox_state");

            migrationBuilder.DropPrimaryKey(
                name: "pk_outbox_message",
                table: "outbox_message");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_status_histories",
                table: "order_status_histories");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_refunds",
                table: "order_refunds");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_invoices",
                table: "order_invoices");

            migrationBuilder.DropPrimaryKey(
                name: "pk_order_cancellations",
                table: "order_cancellations");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_inbox_state_message_id_consumer_id",
                table: "inbox_state");

            migrationBuilder.DropPrimaryKey(
                name: "pk_inbox_state",
                table: "inbox_state");

            migrationBuilder.DropPrimaryKey(
                name: "pk_idempotency_keys",
                table: "idempotency_keys");

            migrationBuilder.DropPrimaryKey(
                name: "pk_discount_codes",
                table: "discount_codes");

            migrationBuilder.DropPrimaryKey(
                name: "pk_discount_code_usages",
                table: "discount_code_usages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_discount_code_scopes",
                table: "discount_code_scopes");

            migrationBuilder.DropPrimaryKey(
                name: "pk_daily_order_sequences",
                table: "daily_order_sequences");

            migrationBuilder.DropPrimaryKey(
                name: "pk_cart_items",
                table: "cart_items");

            migrationBuilder.DropPrimaryKey(
                name: "pk_audit_logs",
                table: "audit_logs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_auction_orders",
                table: "auction_orders");

            migrationBuilder.RenameTable(
                name: "orders",
                newName: "Orders");

            migrationBuilder.RenameTable(
                name: "carts",
                newName: "Carts");

            migrationBuilder.RenameTable(
                name: "system_settings",
                newName: "SystemSettings");

            migrationBuilder.RenameTable(
                name: "sub_orders",
                newName: "SubOrders");

            migrationBuilder.RenameTable(
                name: "sub_order_items",
                newName: "SubOrderItems");

            migrationBuilder.RenameTable(
                name: "outbox_state",
                newName: "OutboxState");

            migrationBuilder.RenameTable(
                name: "outbox_message",
                newName: "OutboxMessage");

            migrationBuilder.RenameTable(
                name: "order_status_histories",
                newName: "OrderStatusHistories");

            migrationBuilder.RenameTable(
                name: "order_refunds",
                newName: "OrderRefunds");

            migrationBuilder.RenameTable(
                name: "order_invoices",
                newName: "OrderInvoices");

            migrationBuilder.RenameTable(
                name: "order_cancellations",
                newName: "OrderCancellations");

            migrationBuilder.RenameTable(
                name: "inbox_state",
                newName: "InboxState");

            migrationBuilder.RenameTable(
                name: "idempotency_keys",
                newName: "IdempotencyKeys");

            migrationBuilder.RenameTable(
                name: "discount_codes",
                newName: "DiscountCodes");

            migrationBuilder.RenameTable(
                name: "discount_code_usages",
                newName: "DiscountCodeUsages");

            migrationBuilder.RenameTable(
                name: "discount_code_scopes",
                newName: "DiscountCodeScopes");

            migrationBuilder.RenameTable(
                name: "daily_order_sequences",
                newName: "DailyOrderSequences");

            migrationBuilder.RenameTable(
                name: "cart_items",
                newName: "CartItems");

            migrationBuilder.RenameTable(
                name: "audit_logs",
                newName: "AuditLogs");

            migrationBuilder.RenameTable(
                name: "auction_orders",
                newName: "AuctionOrders");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "Orders",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Orders",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "wallet_transaction_id",
                table: "Orders",
                newName: "WalletTransactionId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Orders",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "total_amount",
                table: "Orders",
                newName: "TotalAmount");

            migrationBuilder.RenameColumn(
                name: "row_version",
                table: "Orders",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "payment_deadline_at",
                table: "Orders",
                newName: "PaymentDeadlineAt");

            migrationBuilder.RenameColumn(
                name: "payment_attempt_count",
                table: "Orders",
                newName: "PaymentAttemptCount");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "Orders",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "Orders",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Orders",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "cart_id",
                table: "Orders",
                newName: "CartId");

            migrationBuilder.RenameColumn(
                name: "buyer_id",
                table: "Orders",
                newName: "BuyerId");

            migrationBuilder.RenameIndex(
                name: "ix_orders_cart_id",
                table: "Orders",
                newName: "IX_Orders_CartId");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "Carts",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Carts",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Carts",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "subtotal_amount",
                table: "Carts",
                newName: "SubtotalAmount");

            migrationBuilder.RenameColumn(
                name: "row_version",
                table: "Carts",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "last_activity_at",
                table: "Carts",
                newName: "LastActivityAt");

            migrationBuilder.RenameColumn(
                name: "item_count",
                table: "Carts",
                newName: "ItemCount");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "Carts",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "Carts",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Carts",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "buyer_id",
                table: "Carts",
                newName: "BuyerId");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "SystemSettings",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "category",
                table: "SystemSettings",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SystemSettings",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "value_type",
                table: "SystemSettings",
                newName: "ValueType");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "SystemSettings",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "SystemSettings",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "setting_value",
                table: "SystemSettings",
                newName: "SettingValue");

            migrationBuilder.RenameColumn(
                name: "setting_key",
                table: "SystemSettings",
                newName: "SettingKey");

            migrationBuilder.RenameColumn(
                name: "is_editable",
                table: "SystemSettings",
                newName: "IsEditable");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "SystemSettings",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "SystemSettings",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "SystemSettings",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_system_settings_setting_key",
                table: "SystemSettings",
                newName: "IX_SystemSettings_SettingKey");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "SubOrders",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SubOrders",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "wallet_transaction_id",
                table: "SubOrders",
                newName: "WalletTransactionId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "SubOrders",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "total_amount",
                table: "SubOrders",
                newName: "TotalAmount");

            migrationBuilder.RenameColumn(
                name: "system_discount_amount",
                table: "SubOrders",
                newName: "SystemDiscountAmount");

            migrationBuilder.RenameColumn(
                name: "sub_order_no",
                table: "SubOrders",
                newName: "SubOrderNo");

            migrationBuilder.RenameColumn(
                name: "shop_discount_amount",
                table: "SubOrders",
                newName: "ShopDiscountAmount");

            migrationBuilder.RenameColumn(
                name: "shipping_fee_amount",
                table: "SubOrders",
                newName: "ShippingFeeAmount");

            migrationBuilder.RenameColumn(
                name: "shipping_address",
                table: "SubOrders",
                newName: "ShippingAddress");

            migrationBuilder.RenameColumn(
                name: "shipped_at",
                table: "SubOrders",
                newName: "ShippedAt");

            migrationBuilder.RenameColumn(
                name: "seller_respond_deadline_at",
                table: "SubOrders",
                newName: "SellerRespondDeadlineAt");

            migrationBuilder.RenameColumn(
                name: "seller_receive_amount",
                table: "SubOrders",
                newName: "SellerReceiveAmount");

            migrationBuilder.RenameColumn(
                name: "seller_id",
                table: "SubOrders",
                newName: "SellerId");

            migrationBuilder.RenameColumn(
                name: "seller_accepted_at",
                table: "SubOrders",
                newName: "SellerAcceptedAt");

            migrationBuilder.RenameColumn(
                name: "row_version",
                table: "SubOrders",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "product_discount_amount",
                table: "SubOrders",
                newName: "ProductDiscountAmount");

            migrationBuilder.RenameColumn(
                name: "platform_markup_amount",
                table: "SubOrders",
                newName: "PlatformMarkupAmount");

            migrationBuilder.RenameColumn(
                name: "platform_fee_amount",
                table: "SubOrders",
                newName: "PlatformFeeAmount");

            migrationBuilder.RenameColumn(
                name: "payment_method",
                table: "SubOrders",
                newName: "PaymentMethod");

            migrationBuilder.RenameColumn(
                name: "payment_deadline_at",
                table: "SubOrders",
                newName: "PaymentDeadlineAt");

            migrationBuilder.RenameColumn(
                name: "paid_at",
                table: "SubOrders",
                newName: "PaidAt");

            migrationBuilder.RenameColumn(
                name: "order_id",
                table: "SubOrders",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "items_amount",
                table: "SubOrders",
                newName: "ItemsAmount");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "SubOrders",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "delivered_at",
                table: "SubOrders",
                newName: "DeliveredAt");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "SubOrders",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "SubOrders",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "contact_phone",
                table: "SubOrders",
                newName: "ContactPhone");

            migrationBuilder.RenameColumn(
                name: "completed_at",
                table: "SubOrders",
                newName: "CompletedAt");

            migrationBuilder.RenameColumn(
                name: "cancelled_at",
                table: "SubOrders",
                newName: "CancelledAt");

            migrationBuilder.RenameColumn(
                name: "buyer_id",
                table: "SubOrders",
                newName: "BuyerId");

            migrationBuilder.RenameIndex(
                name: "ix_sub_orders_sub_order_no",
                table: "SubOrders",
                newName: "IX_SubOrders_SubOrderNo");

            migrationBuilder.RenameIndex(
                name: "ix_sub_orders_order_id",
                table: "SubOrders",
                newName: "IX_SubOrders_OrderId");

            migrationBuilder.RenameColumn(
                name: "quantity",
                table: "SubOrderItems",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SubOrderItems",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "SubOrderItems",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "unit_price_after_discount",
                table: "SubOrderItems",
                newName: "UnitPriceAfterDiscount");

            migrationBuilder.RenameColumn(
                name: "unit_price",
                table: "SubOrderItems",
                newName: "UnitPrice");

            migrationBuilder.RenameColumn(
                name: "sub_order_id",
                table: "SubOrderItems",
                newName: "SubOrderId");

            migrationBuilder.RenameColumn(
                name: "sku_id",
                table: "SubOrderItems",
                newName: "SkuId");

            migrationBuilder.RenameColumn(
                name: "sku_code_snapshot",
                table: "SubOrderItems",
                newName: "SkuCodeSnapshot");

            migrationBuilder.RenameColumn(
                name: "sku_attributes_snapshot",
                table: "SubOrderItems",
                newName: "SkuAttributesSnapshot");

            migrationBuilder.RenameColumn(
                name: "seller_id",
                table: "SubOrderItems",
                newName: "SellerId");

            migrationBuilder.RenameColumn(
                name: "product_name_snapshot",
                table: "SubOrderItems",
                newName: "ProductNameSnapshot");

            migrationBuilder.RenameColumn(
                name: "product_id",
                table: "SubOrderItems",
                newName: "ProductId");

            migrationBuilder.RenameColumn(
                name: "line_total",
                table: "SubOrderItems",
                newName: "LineTotal");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "SubOrderItems",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "image_url_snapshot",
                table: "SubOrderItems",
                newName: "ImageUrlSnapshot");

            migrationBuilder.RenameColumn(
                name: "discount_code_id",
                table: "SubOrderItems",
                newName: "DiscountCodeId");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "SubOrderItems",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "SubOrderItems",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_sub_order_items_sub_order_id",
                table: "SubOrderItems",
                newName: "IX_SubOrderItems_SubOrderId");

            migrationBuilder.RenameColumn(
                name: "delivered",
                table: "OutboxState",
                newName: "Delivered");

            migrationBuilder.RenameColumn(
                name: "created",
                table: "OutboxState",
                newName: "Created");

            migrationBuilder.RenameColumn(
                name: "row_version",
                table: "OutboxState",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "lock_id",
                table: "OutboxState",
                newName: "LockId");

            migrationBuilder.RenameColumn(
                name: "last_sequence_number",
                table: "OutboxState",
                newName: "LastSequenceNumber");

            migrationBuilder.RenameColumn(
                name: "outbox_id",
                table: "OutboxState",
                newName: "OutboxId");

            migrationBuilder.RenameIndex(
                name: "ix_outbox_state_created",
                table: "OutboxState",
                newName: "IX_OutboxState_Created");

            migrationBuilder.RenameColumn(
                name: "properties",
                table: "OutboxMessage",
                newName: "Properties");

            migrationBuilder.RenameColumn(
                name: "headers",
                table: "OutboxMessage",
                newName: "Headers");

            migrationBuilder.RenameColumn(
                name: "body",
                table: "OutboxMessage",
                newName: "Body");

            migrationBuilder.RenameColumn(
                name: "source_address",
                table: "OutboxMessage",
                newName: "SourceAddress");

            migrationBuilder.RenameColumn(
                name: "sent_time",
                table: "OutboxMessage",
                newName: "SentTime");

            migrationBuilder.RenameColumn(
                name: "response_address",
                table: "OutboxMessage",
                newName: "ResponseAddress");

            migrationBuilder.RenameColumn(
                name: "request_id",
                table: "OutboxMessage",
                newName: "RequestId");

            migrationBuilder.RenameColumn(
                name: "outbox_id",
                table: "OutboxMessage",
                newName: "OutboxId");

            migrationBuilder.RenameColumn(
                name: "message_type",
                table: "OutboxMessage",
                newName: "MessageType");

            migrationBuilder.RenameColumn(
                name: "message_id",
                table: "OutboxMessage",
                newName: "MessageId");

            migrationBuilder.RenameColumn(
                name: "initiator_id",
                table: "OutboxMessage",
                newName: "InitiatorId");

            migrationBuilder.RenameColumn(
                name: "inbox_message_id",
                table: "OutboxMessage",
                newName: "InboxMessageId");

            migrationBuilder.RenameColumn(
                name: "inbox_consumer_id",
                table: "OutboxMessage",
                newName: "InboxConsumerId");

            migrationBuilder.RenameColumn(
                name: "fault_address",
                table: "OutboxMessage",
                newName: "FaultAddress");

            migrationBuilder.RenameColumn(
                name: "expiration_time",
                table: "OutboxMessage",
                newName: "ExpirationTime");

            migrationBuilder.RenameColumn(
                name: "enqueue_time",
                table: "OutboxMessage",
                newName: "EnqueueTime");

            migrationBuilder.RenameColumn(
                name: "destination_address",
                table: "OutboxMessage",
                newName: "DestinationAddress");

            migrationBuilder.RenameColumn(
                name: "correlation_id",
                table: "OutboxMessage",
                newName: "CorrelationId");

            migrationBuilder.RenameColumn(
                name: "conversation_id",
                table: "OutboxMessage",
                newName: "ConversationId");

            migrationBuilder.RenameColumn(
                name: "content_type",
                table: "OutboxMessage",
                newName: "ContentType");

            migrationBuilder.RenameColumn(
                name: "sequence_number",
                table: "OutboxMessage",
                newName: "SequenceNumber");

            migrationBuilder.RenameIndex(
                name: "ix_outbox_message_outbox_id_sequence_number",
                table: "OutboxMessage",
                newName: "IX_OutboxMessage_OutboxId_SequenceNumber");

            migrationBuilder.RenameIndex(
                name: "ix_outbox_message_inbox_message_id_inbox_consumer_id_sequence_",
                table: "OutboxMessage",
                newName: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber");

            migrationBuilder.RenameIndex(
                name: "ix_outbox_message_expiration_time",
                table: "OutboxMessage",
                newName: "IX_OutboxMessage_ExpirationTime");

            migrationBuilder.RenameIndex(
                name: "ix_outbox_message_enqueue_time",
                table: "OutboxMessage",
                newName: "IX_OutboxMessage_EnqueueTime");

            migrationBuilder.RenameColumn(
                name: "reason",
                table: "OrderStatusHistories",
                newName: "Reason");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "OrderStatusHistories",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "OrderStatusHistories",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "to_status",
                table: "OrderStatusHistories",
                newName: "ToStatus");

            migrationBuilder.RenameColumn(
                name: "sub_order_id",
                table: "OrderStatusHistories",
                newName: "SubOrderId");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "OrderStatusHistories",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "from_status",
                table: "OrderStatusHistories",
                newName: "FromStatus");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "OrderStatusHistories",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "OrderStatusHistories",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "changed_by",
                table: "OrderStatusHistories",
                newName: "ChangedBy");

            migrationBuilder.RenameColumn(
                name: "auction_order_id",
                table: "OrderStatusHistories",
                newName: "AuctionOrderId");

            migrationBuilder.RenameColumn(
                name: "actor_role",
                table: "OrderStatusHistories",
                newName: "ActorRole");

            migrationBuilder.RenameIndex(
                name: "ix_order_status_histories_sub_order_id",
                table: "OrderStatusHistories",
                newName: "IX_OrderStatusHistories_SubOrderId");

            migrationBuilder.RenameIndex(
                name: "ix_order_status_histories_auction_order_id",
                table: "OrderStatusHistories",
                newName: "IX_OrderStatusHistories_AuctionOrderId");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "OrderRefunds",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "amount",
                table: "OrderRefunds",
                newName: "Amount");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "OrderRefunds",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "wallet_transaction_id",
                table: "OrderRefunds",
                newName: "WalletTransactionId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "OrderRefunds",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "sub_order_id",
                table: "OrderRefunds",
                newName: "SubOrderId");

            migrationBuilder.RenameColumn(
                name: "row_version",
                table: "OrderRefunds",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "requested_by",
                table: "OrderRefunds",
                newName: "RequestedBy");

            migrationBuilder.RenameColumn(
                name: "requested_at",
                table: "OrderRefunds",
                newName: "RequestedAt");

            migrationBuilder.RenameColumn(
                name: "request_event_id",
                table: "OrderRefunds",
                newName: "RequestEventId");

            migrationBuilder.RenameColumn(
                name: "refund_type",
                table: "OrderRefunds",
                newName: "RefundType");

            migrationBuilder.RenameColumn(
                name: "refund_transaction_id",
                table: "OrderRefunds",
                newName: "RefundTransactionId");

            migrationBuilder.RenameColumn(
                name: "reason_note",
                table: "OrderRefunds",
                newName: "ReasonNote");

            migrationBuilder.RenameColumn(
                name: "reason_code",
                table: "OrderRefunds",
                newName: "ReasonCode");

            migrationBuilder.RenameColumn(
                name: "order_id",
                table: "OrderRefunds",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "OrderRefunds",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "failure_code",
                table: "OrderRefunds",
                newName: "FailureCode");

            migrationBuilder.RenameColumn(
                name: "failed_at",
                table: "OrderRefunds",
                newName: "FailedAt");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "OrderRefunds",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "OrderRefunds",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "completed_at",
                table: "OrderRefunds",
                newName: "CompletedAt");

            migrationBuilder.RenameColumn(
                name: "cancellation_id",
                table: "OrderRefunds",
                newName: "CancellationId");

            migrationBuilder.RenameColumn(
                name: "auction_order_id",
                table: "OrderRefunds",
                newName: "AuctionOrderId");

            migrationBuilder.RenameColumn(
                name: "actor_role",
                table: "OrderRefunds",
                newName: "ActorRole");

            migrationBuilder.RenameIndex(
                name: "ix_order_refunds_sub_order_id",
                table: "OrderRefunds",
                newName: "IX_OrderRefunds_SubOrderId");

            migrationBuilder.RenameIndex(
                name: "ix_order_refunds_order_id",
                table: "OrderRefunds",
                newName: "IX_OrderRefunds_OrderId");

            migrationBuilder.RenameIndex(
                name: "ix_order_refunds_cancellation_id",
                table: "OrderRefunds",
                newName: "IX_OrderRefunds_CancellationId");

            migrationBuilder.RenameIndex(
                name: "ix_order_refunds_auction_order_id",
                table: "OrderRefunds",
                newName: "IX_OrderRefunds_AuctionOrderId");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "OrderInvoices",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "OrderInvoices",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "OrderInvoices",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "total_amount",
                table: "OrderInvoices",
                newName: "TotalAmount");

            migrationBuilder.RenameColumn(
                name: "sub_order_id",
                table: "OrderInvoices",
                newName: "SubOrderId");

            migrationBuilder.RenameColumn(
                name: "order_id",
                table: "OrderInvoices",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "issued_at",
                table: "OrderInvoices",
                newName: "IssuedAt");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "OrderInvoices",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "invoice_no",
                table: "OrderInvoices",
                newName: "InvoiceNo");

            migrationBuilder.RenameColumn(
                name: "file_url",
                table: "OrderInvoices",
                newName: "FileUrl");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "OrderInvoices",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "OrderInvoices",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "auction_order_id",
                table: "OrderInvoices",
                newName: "AuctionOrderId");

            migrationBuilder.RenameIndex(
                name: "ix_order_invoices_sub_order_id",
                table: "OrderInvoices",
                newName: "IX_OrderInvoices_SubOrderId");

            migrationBuilder.RenameIndex(
                name: "ix_order_invoices_order_id",
                table: "OrderInvoices",
                newName: "IX_OrderInvoices_OrderId");

            migrationBuilder.RenameIndex(
                name: "ix_order_invoices_invoice_no",
                table: "OrderInvoices",
                newName: "IX_OrderInvoices_InvoiceNo");

            migrationBuilder.RenameIndex(
                name: "ix_order_invoices_auction_order_id",
                table: "OrderInvoices",
                newName: "IX_OrderInvoices_AuctionOrderId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "OrderCancellations",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "was_paid",
                table: "OrderCancellations",
                newName: "WasPaid");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "OrderCancellations",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "sub_order_id",
                table: "OrderCancellations",
                newName: "SubOrderId");

            migrationBuilder.RenameColumn(
                name: "refund_transaction_id",
                table: "OrderCancellations",
                newName: "RefundTransactionId");

            migrationBuilder.RenameColumn(
                name: "reason_note",
                table: "OrderCancellations",
                newName: "ReasonNote");

            migrationBuilder.RenameColumn(
                name: "reason_code",
                table: "OrderCancellations",
                newName: "ReasonCode");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "OrderCancellations",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "inventory_released",
                table: "OrderCancellations",
                newName: "InventoryReleased");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "OrderCancellations",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "OrderCancellations",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "cancelled_by",
                table: "OrderCancellations",
                newName: "CancelledBy");

            migrationBuilder.RenameColumn(
                name: "auction_order_id",
                table: "OrderCancellations",
                newName: "AuctionOrderId");

            migrationBuilder.RenameColumn(
                name: "actor_role",
                table: "OrderCancellations",
                newName: "ActorRole");

            migrationBuilder.RenameIndex(
                name: "ix_order_cancellations_sub_order_id",
                table: "OrderCancellations",
                newName: "IX_OrderCancellations_SubOrderId");

            migrationBuilder.RenameIndex(
                name: "ix_order_cancellations_auction_order_id",
                table: "OrderCancellations",
                newName: "IX_OrderCancellations_AuctionOrderId");

            migrationBuilder.RenameColumn(
                name: "received",
                table: "InboxState",
                newName: "Received");

            migrationBuilder.RenameColumn(
                name: "delivered",
                table: "InboxState",
                newName: "Delivered");

            migrationBuilder.RenameColumn(
                name: "consumed",
                table: "InboxState",
                newName: "Consumed");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "InboxState",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "row_version",
                table: "InboxState",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "receive_count",
                table: "InboxState",
                newName: "ReceiveCount");

            migrationBuilder.RenameColumn(
                name: "message_id",
                table: "InboxState",
                newName: "MessageId");

            migrationBuilder.RenameColumn(
                name: "lock_id",
                table: "InboxState",
                newName: "LockId");

            migrationBuilder.RenameColumn(
                name: "last_sequence_number",
                table: "InboxState",
                newName: "LastSequenceNumber");

            migrationBuilder.RenameColumn(
                name: "expiration_time",
                table: "InboxState",
                newName: "ExpirationTime");

            migrationBuilder.RenameColumn(
                name: "consumer_id",
                table: "InboxState",
                newName: "ConsumerId");

            migrationBuilder.RenameIndex(
                name: "ix_inbox_state_delivered",
                table: "InboxState",
                newName: "IX_InboxState_Delivered");

            migrationBuilder.RenameColumn(
                name: "scope",
                table: "IdempotencyKeys",
                newName: "Scope");

            migrationBuilder.RenameColumn(
                name: "key",
                table: "IdempotencyKeys",
                newName: "Key");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "IdempotencyKeys",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "IdempotencyKeys",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "response_status_code",
                table: "IdempotencyKeys",
                newName: "ResponseStatusCode");

            migrationBuilder.RenameColumn(
                name: "response_body",
                table: "IdempotencyKeys",
                newName: "ResponseBody");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "IdempotencyKeys",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "IdempotencyKeys",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "IdempotencyKeys",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "IdempotencyKeys",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_idempotency_keys_key_scope",
                table: "IdempotencyKeys",
                newName: "IX_IdempotencyKeys_Key_Scope");

            migrationBuilder.RenameIndex(
                name: "ix_idempotency_keys_expires_at",
                table: "IdempotencyKeys",
                newName: "IX_IdempotencyKeys_ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "DiscountCodes",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "DiscountCodes",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "DiscountCodes",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "DiscountCodes",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "used_count",
                table: "DiscountCodes",
                newName: "UsedCount");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "DiscountCodes",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "starts_at",
                table: "DiscountCodes",
                newName: "StartsAt");

            migrationBuilder.RenameColumn(
                name: "scope_type",
                table: "DiscountCodes",
                newName: "ScopeType");

            migrationBuilder.RenameColumn(
                name: "reserved_count",
                table: "DiscountCodes",
                newName: "ReservedCount");

            migrationBuilder.RenameColumn(
                name: "owner_seller_id",
                table: "DiscountCodes",
                newName: "OwnerSellerId");

            migrationBuilder.RenameColumn(
                name: "min_order_amount",
                table: "DiscountCodes",
                newName: "MinOrderAmount");

            migrationBuilder.RenameColumn(
                name: "max_uses_total",
                table: "DiscountCodes",
                newName: "MaxUsesTotal");

            migrationBuilder.RenameColumn(
                name: "max_uses_per_user",
                table: "DiscountCodes",
                newName: "MaxUsesPerUser");

            migrationBuilder.RenameColumn(
                name: "max_discount_amount",
                table: "DiscountCodes",
                newName: "MaxDiscountAmount");

            migrationBuilder.RenameColumn(
                name: "is_visible",
                table: "DiscountCodes",
                newName: "IsVisible");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "DiscountCodes",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "ends_at",
                table: "DiscountCodes",
                newName: "EndsAt");

            migrationBuilder.RenameColumn(
                name: "discount_value",
                table: "DiscountCodes",
                newName: "DiscountValue");

            migrationBuilder.RenameColumn(
                name: "discount_type",
                table: "DiscountCodes",
                newName: "DiscountType");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "DiscountCodes",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "DiscountCodes",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "DiscountCodes",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_discount_codes_code",
                table: "DiscountCodes",
                newName: "IX_DiscountCodes_Code");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "DiscountCodeUsages",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "DiscountCodeUsages",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "DiscountCodeUsages",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "used_at",
                table: "DiscountCodeUsages",
                newName: "UsedAt");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "DiscountCodeUsages",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "sub_order_id",
                table: "DiscountCodeUsages",
                newName: "SubOrderId");

            migrationBuilder.RenameColumn(
                name: "reserved_at",
                table: "DiscountCodeUsages",
                newName: "ReservedAt");

            migrationBuilder.RenameColumn(
                name: "released_at",
                table: "DiscountCodeUsages",
                newName: "ReleasedAt");

            migrationBuilder.RenameColumn(
                name: "order_id",
                table: "DiscountCodeUsages",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "DiscountCodeUsages",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "discount_code_id",
                table: "DiscountCodeUsages",
                newName: "DiscountCodeId");

            migrationBuilder.RenameColumn(
                name: "discount_amount",
                table: "DiscountCodeUsages",
                newName: "DiscountAmount");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "DiscountCodeUsages",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "DiscountCodeUsages",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_discount_code_usages_sub_order_id",
                table: "DiscountCodeUsages",
                newName: "IX_DiscountCodeUsages_SubOrderId");

            migrationBuilder.RenameIndex(
                name: "ix_discount_code_usages_order_id",
                table: "DiscountCodeUsages",
                newName: "IX_DiscountCodeUsages_OrderId");

            migrationBuilder.RenameIndex(
                name: "ix_discount_code_usages_discount_code_id",
                table: "DiscountCodeUsages",
                newName: "IX_DiscountCodeUsages_DiscountCodeId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "DiscountCodeScopes",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "DiscountCodeScopes",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "scope_ref_type",
                table: "DiscountCodeScopes",
                newName: "ScopeRefType");

            migrationBuilder.RenameColumn(
                name: "scope_ref_id",
                table: "DiscountCodeScopes",
                newName: "ScopeRefId");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "DiscountCodeScopes",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "discount_code_id",
                table: "DiscountCodeScopes",
                newName: "DiscountCodeId");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "DiscountCodeScopes",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "DiscountCodeScopes",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_discount_code_scopes_discount_code_id",
                table: "DiscountCodeScopes",
                newName: "IX_DiscountCodeScopes_DiscountCodeId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "DailyOrderSequences",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "last_value",
                table: "DailyOrderSequences",
                newName: "LastValue");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "DailyOrderSequences",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "seq_type",
                table: "DailyOrderSequences",
                newName: "SeqType");

            migrationBuilder.RenameColumn(
                name: "seq_date",
                table: "DailyOrderSequences",
                newName: "SeqDate");

            migrationBuilder.RenameColumn(
                name: "quantity",
                table: "CartItems",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "CartItems",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "CartItems",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "unit_price_snapshot",
                table: "CartItems",
                newName: "UnitPriceSnapshot");

            migrationBuilder.RenameColumn(
                name: "sku_id",
                table: "CartItems",
                newName: "SkuId");

            migrationBuilder.RenameColumn(
                name: "seller_id",
                table: "CartItems",
                newName: "SellerId");

            migrationBuilder.RenameColumn(
                name: "product_name_snapshot",
                table: "CartItems",
                newName: "ProductNameSnapshot");

            migrationBuilder.RenameColumn(
                name: "product_id",
                table: "CartItems",
                newName: "ProductId");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "CartItems",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "CartItems",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "CartItems",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "cart_id",
                table: "CartItems",
                newName: "CartId");

            migrationBuilder.RenameColumn(
                name: "added_at",
                table: "CartItems",
                newName: "AddedAt");

            migrationBuilder.RenameIndex(
                name: "ix_cart_items_product_id",
                table: "CartItems",
                newName: "IX_CartItems_ProductId");

            migrationBuilder.RenameIndex(
                name: "ix_cart_items_cart_id_sku_id",
                table: "CartItems",
                newName: "IX_CartItems_CartId_SkuId");

            migrationBuilder.RenameColumn(
                name: "action",
                table: "AuditLogs",
                newName: "Action");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AuditLogs",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_name",
                table: "AuditLogs",
                newName: "UserName");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "AuditLogs",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "user_agent",
                table: "AuditLogs",
                newName: "UserAgent");

            migrationBuilder.RenameColumn(
                name: "trace_id",
                table: "AuditLogs",
                newName: "TraceId");

            migrationBuilder.RenameColumn(
                name: "service_name",
                table: "AuditLogs",
                newName: "ServiceName");

            migrationBuilder.RenameColumn(
                name: "old_values",
                table: "AuditLogs",
                newName: "OldValues");

            migrationBuilder.RenameColumn(
                name: "new_values",
                table: "AuditLogs",
                newName: "NewValues");

            migrationBuilder.RenameColumn(
                name: "is_synced",
                table: "AuditLogs",
                newName: "IsSynced");

            migrationBuilder.RenameColumn(
                name: "ip_address",
                table: "AuditLogs",
                newName: "IpAddress");

            migrationBuilder.RenameColumn(
                name: "entity_name",
                table: "AuditLogs",
                newName: "EntityName");

            migrationBuilder.RenameColumn(
                name: "entity_id",
                table: "AuditLogs",
                newName: "EntityId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "AuditLogs",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "changed_columns",
                table: "AuditLogs",
                newName: "ChangedColumns");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "AuctionOrders",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AuctionOrders",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "winning_bid_id",
                table: "AuctionOrders",
                newName: "WinningBidId");

            migrationBuilder.RenameColumn(
                name: "winning_amount",
                table: "AuctionOrders",
                newName: "WinningAmount");

            migrationBuilder.RenameColumn(
                name: "wallet_transaction_id",
                table: "AuctionOrders",
                newName: "WalletTransactionId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "AuctionOrders",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "total_amount",
                table: "AuctionOrders",
                newName: "TotalAmount");

            migrationBuilder.RenameColumn(
                name: "sku_id",
                table: "AuctionOrders",
                newName: "SkuId");

            migrationBuilder.RenameColumn(
                name: "sku_attributes_snapshot",
                table: "AuctionOrders",
                newName: "SkuAttributesSnapshot");

            migrationBuilder.RenameColumn(
                name: "shipping_fee_amount",
                table: "AuctionOrders",
                newName: "ShippingFeeAmount");

            migrationBuilder.RenameColumn(
                name: "shipping_address",
                table: "AuctionOrders",
                newName: "ShippingAddress");

            migrationBuilder.RenameColumn(
                name: "shipped_at",
                table: "AuctionOrders",
                newName: "ShippedAt");

            migrationBuilder.RenameColumn(
                name: "seller_respond_deadline_at",
                table: "AuctionOrders",
                newName: "SellerRespondDeadlineAt");

            migrationBuilder.RenameColumn(
                name: "seller_receive_amount",
                table: "AuctionOrders",
                newName: "SellerReceiveAmount");

            migrationBuilder.RenameColumn(
                name: "seller_id",
                table: "AuctionOrders",
                newName: "SellerId");

            migrationBuilder.RenameColumn(
                name: "seller_accepted_at",
                table: "AuctionOrders",
                newName: "SellerAcceptedAt");

            migrationBuilder.RenameColumn(
                name: "row_version",
                table: "AuctionOrders",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "product_name_snapshot",
                table: "AuctionOrders",
                newName: "ProductNameSnapshot");

            migrationBuilder.RenameColumn(
                name: "product_id",
                table: "AuctionOrders",
                newName: "ProductId");

            migrationBuilder.RenameColumn(
                name: "platform_fee_amount",
                table: "AuctionOrders",
                newName: "PlatformFeeAmount");

            migrationBuilder.RenameColumn(
                name: "payment_method",
                table: "AuctionOrders",
                newName: "PaymentMethod");

            migrationBuilder.RenameColumn(
                name: "payment_deadline_at",
                table: "AuctionOrders",
                newName: "PaymentDeadlineAt");

            migrationBuilder.RenameColumn(
                name: "paid_at",
                table: "AuctionOrders",
                newName: "PaidAt");

            migrationBuilder.RenameColumn(
                name: "order_no",
                table: "AuctionOrders",
                newName: "OrderNo");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "AuctionOrders",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "image_url_snapshot",
                table: "AuctionOrders",
                newName: "ImageUrlSnapshot");

            migrationBuilder.RenameColumn(
                name: "delivered_at",
                table: "AuctionOrders",
                newName: "DeliveredAt");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                table: "AuctionOrders",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "AuctionOrders",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "contact_phone",
                table: "AuctionOrders",
                newName: "ContactPhone");

            migrationBuilder.RenameColumn(
                name: "completed_at",
                table: "AuctionOrders",
                newName: "CompletedAt");

            migrationBuilder.RenameColumn(
                name: "cancelled_at",
                table: "AuctionOrders",
                newName: "CancelledAt");

            migrationBuilder.RenameColumn(
                name: "buyer_id",
                table: "AuctionOrders",
                newName: "BuyerId");

            migrationBuilder.RenameColumn(
                name: "auction_id",
                table: "AuctionOrders",
                newName: "AuctionId");

            migrationBuilder.RenameIndex(
                name: "ix_auction_orders_order_no",
                table: "AuctionOrders",
                newName: "IX_AuctionOrders_OrderNo");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Orders",
                table: "Orders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Carts",
                table: "Carts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SystemSettings",
                table: "SystemSettings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubOrders",
                table: "SubOrders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubOrderItems",
                table: "SubOrderItems",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OutboxState",
                table: "OutboxState",
                column: "OutboxId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OutboxMessage",
                table: "OutboxMessage",
                column: "SequenceNumber");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderStatusHistories",
                table: "OrderStatusHistories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderRefunds",
                table: "OrderRefunds",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderInvoices",
                table: "OrderInvoices",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrderCancellations",
                table: "OrderCancellations",
                column: "Id");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InboxState_MessageId_ConsumerId",
                table: "InboxState",
                columns: new[] { "MessageId", "ConsumerId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_InboxState",
                table: "InboxState",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IdempotencyKeys",
                table: "IdempotencyKeys",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DiscountCodes",
                table: "DiscountCodes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DiscountCodeUsages",
                table: "DiscountCodeUsages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DiscountCodeScopes",
                table: "DiscountCodeScopes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DailyOrderSequences",
                table: "DailyOrderSequences",
                columns: new[] { "SeqDate", "SeqType" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_CartItems",
                table: "CartItems",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuditLogs",
                table: "AuditLogs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuctionOrders",
                table: "AuctionOrders",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_Carts_CartId",
                table: "CartItems",
                column: "CartId",
                principalTable: "Carts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountCodeScopes_DiscountCodes_DiscountCodeId",
                table: "DiscountCodeScopes",
                column: "DiscountCodeId",
                principalTable: "DiscountCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountCodeUsages_DiscountCodes_DiscountCodeId",
                table: "DiscountCodeUsages",
                column: "DiscountCodeId",
                principalTable: "DiscountCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountCodeUsages_Orders_OrderId",
                table: "DiscountCodeUsages",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountCodeUsages_SubOrders_SubOrderId",
                table: "DiscountCodeUsages",
                column: "SubOrderId",
                principalTable: "SubOrders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderCancellations_AuctionOrders_AuctionOrderId",
                table: "OrderCancellations",
                column: "AuctionOrderId",
                principalTable: "AuctionOrders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderCancellations_SubOrders_SubOrderId",
                table: "OrderCancellations",
                column: "SubOrderId",
                principalTable: "SubOrders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInvoices_AuctionOrders_AuctionOrderId",
                table: "OrderInvoices",
                column: "AuctionOrderId",
                principalTable: "AuctionOrders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInvoices_Orders_OrderId",
                table: "OrderInvoices",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInvoices_SubOrders_SubOrderId",
                table: "OrderInvoices",
                column: "SubOrderId",
                principalTable: "SubOrders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderRefunds_AuctionOrders_AuctionOrderId",
                table: "OrderRefunds",
                column: "AuctionOrderId",
                principalTable: "AuctionOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderRefunds_OrderCancellations_CancellationId",
                table: "OrderRefunds",
                column: "CancellationId",
                principalTable: "OrderCancellations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderRefunds_Orders_OrderId",
                table: "OrderRefunds",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderRefunds_SubOrders_SubOrderId",
                table: "OrderRefunds",
                column: "SubOrderId",
                principalTable: "SubOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Carts_CartId",
                table: "Orders",
                column: "CartId",
                principalTable: "Carts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderStatusHistories_AuctionOrders_AuctionOrderId",
                table: "OrderStatusHistories",
                column: "AuctionOrderId",
                principalTable: "AuctionOrders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderStatusHistories_SubOrders_SubOrderId",
                table: "OrderStatusHistories",
                column: "SubOrderId",
                principalTable: "SubOrders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId" },
                principalTable: "InboxState",
                principalColumns: new[] { "MessageId", "ConsumerId" });

            migrationBuilder.AddForeignKey(
                name: "FK_OutboxMessage_OutboxState_OutboxId",
                table: "OutboxMessage",
                column: "OutboxId",
                principalTable: "OutboxState",
                principalColumn: "OutboxId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubOrderItems_SubOrders_SubOrderId",
                table: "SubOrderItems",
                column: "SubOrderId",
                principalTable: "SubOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SubOrders_Orders_OrderId",
                table: "SubOrders",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
