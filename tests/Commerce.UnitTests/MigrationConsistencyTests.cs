namespace Commerce.UnitTests;

using System;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Commerce.Infrastructure.Persistence;
using Xunit;

public class MigrationConsistencyTests
{
    [Fact]
    public void Snapshot_MatchesCurrentModelForSubOrderIsEscrowSettled()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new AppDbContext(options);
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        var differ = context.GetService<IMigrationsModelDiffer>();

        var snapshot = migrationsAssembly.ModelSnapshot;
        snapshot.Should().NotBeNull("AppDbContextModelSnapshot must exist in Migrations");

        var snapshotModel = snapshot!.Model;
        if (snapshotModel is IMutableModel mutableModel)
        {
            snapshotModel = mutableModel.FinalizeModel();
        }

        var runtimeInitializer = context.GetService<IModelRuntimeInitializer>();
        var finalizedSnapshotModel = runtimeInitializer.Initialize(snapshotModel);
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        var differences = differ.GetDifferences(
            finalizedSnapshotModel.GetRelationalModel(),
            designTimeModel.GetRelationalModel());

        var escrowDifferences = differences.Where(d =>
            d is Microsoft.EntityFrameworkCore.Migrations.Operations.ColumnOperation co &&
            co.Table == "SubOrders" && co.Name == "IsEscrowSettled");

        escrowDifferences.Should().BeEmpty("SubOrders.IsEscrowSettled must have no pending model diffs between model snapshot and current entity configurations.");
    }

    [Fact]
    public void Migrations_ShouldContainAddSubOrderIsEscrowSettled()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new AppDbContext(options);
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();

        var migrations = migrationsAssembly.Migrations;
        migrations.Should().ContainKey("20261118010000_AddSubOrderIsEscrowSettled");
    }

    [Fact]
    public void SubOrderEntity_ShouldHaveIsEscrowSettledConfigured()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;

        using var context = new AppDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(Commerce.Domain.Entities.SubOrder));
        entityType.Should().NotBeNull();

        var prop = entityType!.FindProperty("IsEscrowSettled");
        prop.Should().NotBeNull();
        prop!.ClrType.Should().Be(typeof(bool));
        prop.GetDefaultValue().Should().Be(false);
    }
}
