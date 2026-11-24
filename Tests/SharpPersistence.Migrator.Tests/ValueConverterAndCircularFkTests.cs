using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.EfCore;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class ValueConverterAndCircularFkTests
{
    private readonly record struct CustomerId(Guid Value);

    private enum OrderStatus
    {
        Pending,
        Paid
    }

    private sealed class Customer
    {
        public CustomerId Id { get; set; }
        public Guid? PrimaryOrderId { get; set; }
    }

    private sealed class Order
    {
        public Guid Id { get; set; }
        public CustomerId CustomerId { get; set; }
        public OrderStatus Status { get; set; }
    }

    private sealed class CommerceRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            var customerIdConverter = new ValueConverter<CustomerId, Guid>(v => v.Value, v => new CustomerId(v));

            registry.Entity<Customer>(b =>
            {
                b.ToTable("customers");
                b.Property(x => x.Id).HasConversion(customerIdConverter);
                b.HasForeignKey(x => x.PrimaryOrderId)
                    .References<Order>(o => o.Id)
                    .IsCircular();
            });

            registry.Entity<Order>(b =>
            {
                b.ToTable("orders");
                b.Property(x => x.CustomerId).HasConversion(customerIdConverter);
                b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                b.HasForeignKey(x => x.CustomerId)
                    .References<Customer>(c => c.Id);
            });
        }
    }

    [Fact]
    public void ScanAndDiff_ValueConvertersAndCircularFk_ResolveTypesAndDeferCircularFk()
    {
        var registry = new CommerceRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            [registry]);

        var orderTable = schema.Tables.Single(t => t.TableName == "orders");
        var customerIdCol = orderTable.Columns.Single(c => c.PropertyName == nameof(Order.CustomerId));
        var statusCol = orderTable.Columns.Single(c => c.PropertyName == nameof(Order.Status));

        Assert.Equal("uuid", customerIdCol.SqlType);
        Assert.Equal("character varying(20)", statusCol.SqlType);

        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);
        Assert.True(diff.HasChanges);
        Assert.DoesNotContain("fk_customers_orders_primary_order_id", diff.SqlScript);
        Assert.Contains("fk_customers_orders_primary_order_id", diff.DeferredSqlScript);
        Assert.Contains("fk_orders_customers_customer_id", diff.SqlScript);
    }

    [Fact]
    public void SplitOracleStatements_SplitsPlSqlBlocksAndStandaloneSql()
    {
        const string script = """
            -- Comment line
            DECLARE
                v_cnt NUMBER := 0;
            BEGIN
                SELECT COUNT(*) INTO v_cnt FROM USER_TABLES WHERE TABLE_NAME = 'USERS';
                IF v_cnt = 0 THEN
                    EXECUTE IMMEDIATE 'CREATE TABLE "USERS" ("ID" RAW(16) NOT NULL)';
                END IF;
            END;
            /

            COMMENT ON TABLE "USERS" IS 'User accounts';
            """;

        var statements = MigrationManager.SplitOracleStatements(script).ToList();

        Assert.Equal(2, statements.Count);
        Assert.StartsWith("DECLARE", statements[0]);
        Assert.EndsWith("END;", statements[0]);
        Assert.Equal("COMMENT ON TABLE \"USERS\" IS 'User accounts'", statements[1]);
    }
}
