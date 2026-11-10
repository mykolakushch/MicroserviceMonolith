using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using MicroserviceMonolith.Core.Architecture;
using Xunit;
using LeakyCalculator = MicroserviceMonolith.Tests.BoundaryFixtures.Leaky.Billing.Internal.InvoiceCalculator;

// Fixture modules. "Clean" respects the boundaries, "Leaky" breaks them in three different ways.
namespace MicroserviceMonolith.Tests.BoundaryFixtures.Clean.Billing.Contracts
{
    public record InvoiceIssued(string Id);
}

namespace MicroserviceMonolith.Tests.BoundaryFixtures.Clean.Billing.Internal
{
    public class InvoiceCalculator
    {
        public int Total() => 1;
    }

    public class BillingHandler
    {
        // Using its own module's internals and contracts is fine
        public Contracts.InvoiceIssued Handle() => new(new InvoiceCalculator().Total().ToString());
    }
}

namespace MicroserviceMonolith.Tests.BoundaryFixtures.Clean.Orders
{
    public class OrderService
    {
        // Only the public contract of the other module is used
        public Billing.Contracts.InvoiceIssued? Last { get; set; }

        public string Describe(Billing.Contracts.InvoiceIssued issued) => issued.Id;
    }
}

namespace MicroserviceMonolith.Tests.BoundaryFixtures.Leaky.Billing.Contracts
{
    public record InvoiceIssued(string Id);
}

namespace MicroserviceMonolith.Tests.BoundaryFixtures.Leaky.Billing.Internal
{
    public class InvoiceCalculator
    {
        public int Total() => 1;
    }
}

namespace MicroserviceMonolith.Tests.BoundaryFixtures.Leaky.Orders
{
    public class FieldLeak
    {
        private readonly LeakyCalculator? _calculator = null;

        public bool HasCalculator => _calculator != null;
    }

    public class SignatureLeak
    {
        public LeakyCalculator Make() => new();
    }

    public class BodyLeak
    {
        // Nothing in the signature gives it away: only the method body touches the other module's internals
        public int Run()
        {
            var calculator = new LeakyCalculator();
            return calculator.Total();
        }
    }

    public class ContractUse
    {
        public Billing.Contracts.InvoiceIssued? Last { get; set; }
    }
}

namespace MicroserviceMonolith.Tests
{
    public class ModuleBoundaryTests
    {
        private static readonly Assembly[] Assemblies = { typeof(ModuleBoundaryTests).Assembly };

        private static ModuleBoundary[] Modules(string group) => new[]
        {
            new ModuleBoundary("Orders", $"MicroserviceMonolith.Tests.BoundaryFixtures.{group}.Orders"),
            new ModuleBoundary("Billing", $"MicroserviceMonolith.Tests.BoundaryFixtures.{group}.Billing")
        };

        [Fact]
        public void ModulesThatOnlyUseEachOthersContracts_HaveNoViolations()
        {
            var violations = ModuleBoundaryValidator.FindViolations(Assemblies, Modules("Clean"));

            violations.Should().BeEmpty();
        }

        [Fact]
        public void UsingAnotherModulesInternals_IsReported_ForFieldsSignaturesAndMethodBodies()
        {
            var violations = ModuleBoundaryValidator.FindViolations(Assemblies, Modules("Leaky"));

            var bySource = violations.GroupBy(v => v.SourceType.Name).ToDictionary(g => g.Key, g => g.ToList());
            bySource.Keys.Should().BeEquivalentTo(new[] { "FieldLeak", "SignatureLeak", "BodyLeak" });
            bySource["FieldLeak"].Should().Contain(v => v.Usage.StartsWith("field"));
            bySource["SignatureLeak"].Should().Contain(v => v.Usage.Contains("return type"));
            bySource["BodyLeak"].Should().Contain(v => v.Usage.StartsWith("body of Run"));
            violations.Should().OnlyContain(v => v.TargetType == typeof(LeakyCalculator));
            violations.Should().OnlyContain(v => v.SourceModule.Name == "Orders" && v.TargetModule.Name == "Billing");
        }

        [Fact]
        public void AssertNoViolations_ThrowsWithAReadableListOfViolations()
        {
            Action act = () => ModuleBoundaryValidator.AssertNoViolations(Assemblies, Modules("Leaky"));

            act.Should().Throw<ModuleBoundaryViolationException>()
                .WithMessage("*boundary violation*BodyLeak*InvoiceCalculator*Billing*");
        }

        [Fact]
        public void CustomPublicNamespaces_AreHonoured()
        {
            // Treat the Internal namespace as public: the previous violations disappear
            var modules = new[]
            {
                new ModuleBoundary("Orders", "MicroserviceMonolith.Tests.BoundaryFixtures.Leaky.Orders"),
                new ModuleBoundary(
                    "Billing",
                    "MicroserviceMonolith.Tests.BoundaryFixtures.Leaky.Billing",
                    "MicroserviceMonolith.Tests.BoundaryFixtures.Leaky.Billing.Contracts",
                    "MicroserviceMonolith.Tests.BoundaryFixtures.Leaky.Billing.Internal")
            };

            ModuleBoundaryValidator.FindViolations(Assemblies, modules).Should().BeEmpty();
        }

        [Fact]
        public void TypesOutsideEveryModule_AreUnrestricted()
        {
            // Only Orders is declared a module; Billing types belong to no module, so using them is not a violation
            var modules = new[] { new ModuleBoundary("Orders", "MicroserviceMonolith.Tests.BoundaryFixtures.Leaky.Orders") };

            ModuleBoundaryValidator.FindViolations(Assemblies, modules).Should().BeEmpty();
        }
    }
}
