using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteBus.Messaging;

namespace LiteBus.Mediator.UnitTests.Serialization;

/// <summary>
///     Covers the domain shapes the default message sample factory builds.
/// </summary>
public sealed class MessageSampleFactoryTests
{
    /// <summary>
    ///     Samples a readonly identifier struct through its validating constructor.
    /// </summary>
    [Fact]
    public async Task CreateSample_StructWithValidatingConstructor_ShouldUseTheConstructor()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(OrderId)).ConfigureAwait(false);

        var id = sample.Should().BeOfType<OrderId>().Subject;
        id.Value.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    ///     Diagnoses a record whose guarded string argument accepts no sampled value.
    /// </summary>
    [Fact]
    public async Task CreateSample_RecordWithGuardedString_ShouldDiagnose()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(Money)).ConfigureAwait(false);
        var failure = await factory.DescribeFailureAsync(typeof(Money)).ConfigureAwait(false);

        sample.Should().BeNull();
        failure.Should().NotBeNull();
        failure!.FailedType.Should().Be<Money>();
        failure.MemberPath.Should().BeEmpty();
        failure.Reason.Should().Contain("no static factory or constructor accepts sample values");
    }

    /// <summary>
    ///     Samples an abstract closed set through its first nested case by name.
    /// </summary>
    [Fact]
    public async Task CreateSample_AbstractRecord_ShouldChooseTheFirstNestedCase()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(OrderStatus)).ConfigureAwait(false);

        sample.Should().BeOfType<OrderStatus.Active>();
    }

    /// <summary>
    ///     Samples an abstract closed set deterministically across calls.
    /// </summary>
    [Fact]
    public async Task CreateSample_AbstractRecord_ShouldChooseDeterministically()
    {
        var factory = new DefaultMessageSampleFactory();

        var first = await factory.CreateSampleAsync(typeof(OrderStatus)).ConfigureAwait(false);
        var second = await factory.CreateSampleAsync(typeof(OrderStatus)).ConfigureAwait(false);

        first.Should().BeOfType<OrderStatus.Active>();
        second.Should().BeOfType<OrderStatus.Active>();
    }

    /// <summary>
    ///     Samples an abstract closed set with private case constructors through its instance list.
    /// </summary>
    [Fact]
    public async Task CreateSample_AbstractRecordWithPrivateCases_ShouldUseTheInstanceList()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(ClosedStatus)).ConfigureAwait(false);

        sample.Should().Be(ClosedStatus.All[0]);
    }

    /// <summary>
    ///     Builds a guarded TryParse value from its registered sample.
    /// </summary>
    [Fact]
    public async Task CreateSample_TypeWithGuardedTryParse_ShouldUseRegisteredSample()
    {
        Sku.TryParse("ABC", out var registered).Should().BeTrue();
        var factory = new DefaultMessageSampleFactory(new Dictionary<Type, Func<object?>>
        {
            [typeof(Sku)] = () => registered,
        });

        var sample = await factory.CreateSampleAsync(typeof(Sku)).ConfigureAwait(false);

        var sku = sample.Should().BeOfType<Sku>().Subject;
        sku.Code.Should().Be("ABC");
    }

    /// <summary>
    ///     Samples a struct with no public constructor through its TryCreate factory.
    /// </summary>
    [Fact]
    public async Task CreateSample_TypeWithTryCreate_ShouldUseTheFactory()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(Quantity)).ConfigureAwait(false);

        var quantity = sample.Should().BeOfType<Quantity>().Subject;
        quantity.Value.Should().Be(1);
    }

    /// <summary>
    ///     Samples a class with no public constructor through its Create factory.
    /// </summary>
    [Fact]
    public async Task CreateSample_TypeWithCreate_ShouldUseTheFactory()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(TrackingId)).ConfigureAwait(false);

        var tracking = sample.Should().BeOfType<TrackingId>().Subject;
        tracking.Value.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    ///     Builds a guarded From value from its registered sample.
    /// </summary>
    [Fact]
    public async Task CreateSample_TypeWithGuardedFrom_ShouldUseRegisteredSample()
    {
        var factory = new DefaultMessageSampleFactory(new Dictionary<Type, Func<object?>>
        {
            [typeof(Currency)] = () => Currency.From("EUR"),
        });

        var sample = await factory.CreateSampleAsync(typeof(Currency)).ConfigureAwait(false);

        var currency = sample.Should().BeOfType<Currency>().Subject;
        currency.Code.Should().Be("EUR");
    }

    /// <summary>
    ///     Samples a class with no public constructor through its Parse factory.
    /// </summary>
    [Fact]
    public async Task CreateSample_TypeWithParse_ShouldUseTheFactory()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(Slug)).ConfigureAwait(false);

        var slug = sample.Should().BeOfType<Slug>().Subject;
        slug.Value.Should().Be("sample");
    }

    /// <summary>
    ///     Samples a class with no public constructor through its parameterless Create factory.
    /// </summary>
    [Fact]
    public async Task CreateSample_TypeWithParameterlessFactory_ShouldUseTheFactory()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(ApiKey)).ConfigureAwait(false);

        var key = sample.Should().BeOfType<ApiKey>().Subject;
        key.Value.Should().Be("key-sample");
    }

    /// <summary>
    ///     Diagnoses a guard without a parameter name that rejects the sampled string.
    /// </summary>
    [Fact]
    public async Task CreateSample_GuardWithoutParameterName_ShouldDiagnose()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(Country)).ConfigureAwait(false);
        var failure = await factory.DescribeFailureAsync(typeof(Country)).ConfigureAwait(false);

        sample.Should().BeNull();
        failure.Should().NotBeNull();
        failure!.FailedType.Should().Be<Country>();
        failure.MemberPath.Should().BeEmpty();
        failure.Reason.Should().Contain("no static factory or constructor accepts sample values");
    }

    /// <summary>
    ///     Samples a positional record whose guarded member carries a registered sample.
    /// </summary>
    [Fact]
    public async Task CreateSample_PositionalRecordWithRegisteredMemberSample_ShouldSampleEveryMember()
    {
        var factory = new DefaultMessageSampleFactory(new Dictionary<Type, Func<object?>>
        {
            [typeof(Money)] = () => new Money(1, "EUR"),
        });

        var sample = await factory.CreateSampleAsync(typeof(PlaceOrder)).ConfigureAwait(false);

        var order = sample.Should().BeOfType<PlaceOrder>().Subject;
        order.Id.Value.Should().NotBe(Guid.Empty);
        order.Price.Currency.Should().Be("EUR");
        order.Status.Should().BeOfType<OrderStatus.Active>();
    }

    /// <summary>
    ///     Samples a plain struct through its default value with filled properties.
    /// </summary>
    [Fact]
    public async Task CreateSample_PlainStruct_ShouldFillProperties()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(LinePoint)).ConfigureAwait(false);

        var point = sample.Should().BeOfType<LinePoint>().Subject;
        point.X.Should().Be(1);
        point.Y.Should().Be("sample");
    }

    /// <summary>
    ///     A strongly typed identifier wrapping a Guid with a validating constructor.
    /// </summary>
    private readonly record struct OrderId
    {
        /// <summary>
        ///     Initializes a new identifier, rejecting the empty value.
        /// </summary>
        /// <param name="value">The identifier value, never empty.</param>
        public OrderId(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("The identifier must not be empty.", nameof(value));
            }

            Value = value;
        }

        /// <summary>
        ///     Gets the identifier value.
        /// </summary>
        public Guid Value { get; }
    }

    /// <summary>
    ///     A money-like value object with a validating constructor and a stricter factory.
    /// </summary>
    private sealed record Money
    {
        /// <summary>
        ///     Initializes money, rejecting negative amounts and codes that are not three letters.
        /// </summary>
        /// <param name="amount">The non-negative amount.</param>
        /// <param name="currency">The three-letter currency code.</param>
        public Money(decimal amount, string currency)
        {
            if (amount < 0)
            {
                throw new ArgumentException("The amount must not be negative.", nameof(amount));
            }

            if (currency is not { Length: 3 })
            {
                throw new ArgumentException("The currency code must be three letters.", nameof(currency));
            }

            Amount = amount;
            Currency = currency;
        }

        /// <summary>
        ///     Gets the non-negative amount.
        /// </summary>
        public decimal Amount { get; }

        /// <summary>
        ///     Gets the three-letter currency code.
        /// </summary>
        public string Currency { get; }

        /// <summary>
        ///     Builds money, accepting large amounts only so sampling falls through to the constructor.
        /// </summary>
        /// <param name="amount">The amount, which must exceed one hundred.</param>
        /// <param name="currency">The three-letter currency code.</param>
        public static Money From(decimal amount, string currency)
        {
            if (amount <= 100)
            {
                throw new ArgumentException("The factory only builds large amounts.", nameof(amount));
            }

            return new Money(amount, currency);
        }
    }

    /// <summary>
    ///     A closed set with public nested cases and a code factory that rejects sample codes.
    /// </summary>
    /// <param name="Code">The status code.</param>
    private abstract record OrderStatus(string Code)
    {
        /// <summary>
        ///     Builds a status from a known code.
        /// </summary>
        /// <param name="code">One of the known status codes.</param>
        public static OrderStatus FromCode(string code)
        {
            return code switch
            {
                "ACTIVE" => new Active(),
                "SUSPENDED" => new Suspended(),
                _ => throw new ArgumentException($"Unknown status code '{code}'.", nameof(code)),
            };
        }

        /// <summary>
        ///     The active case.
        /// </summary>
        public sealed record Active() : OrderStatus("ACTIVE");

        /// <summary>
        ///     The suspended case.
        /// </summary>
        public sealed record Suspended() : OrderStatus("SUSPENDED");
    }

    /// <summary>
    ///     A closed set whose cases have private constructors and are exposed through a list.
    /// </summary>
    /// <param name="Code">The status code.</param>
    private abstract record ClosedStatus(string Code)
    {
        /// <summary>
        ///     Gets every known status.
        /// </summary>
        public static IReadOnlyList<ClosedStatus> All { get; } = [new Live(), new Archived()];

        /// <summary>
        ///     Builds a status from a known code.
        /// </summary>
        /// <param name="code">One of the known status codes.</param>
        public static ClosedStatus FromCode(string code)
        {
            return code switch
            {
                "LIVE" => All[0],
                "ARCHIVED" => All[1],
                _ => throw new ArgumentException($"Unknown status code '{code}'.", nameof(code)),
            };
        }

        /// <summary>
        ///     The live case.
        /// </summary>
        public sealed record Live : ClosedStatus
        {
            /// <summary>
            ///     Initializes the live case.
            /// </summary>
            internal Live()
                : base("LIVE")
            {
            }
        }

        /// <summary>
        ///     The archived case.
        /// </summary>
        public sealed record Archived : ClosedStatus
        {
            /// <summary>
            ///     Initializes the archived case.
            /// </summary>
            internal Archived()
                : base("ARCHIVED")
            {
            }
        }
    }

    /// <summary>
    ///     A stock code parsed through TryParse with a three-letter uppercase guard.
    /// </summary>
    private readonly record struct Sku
    {
        /// <summary>
        ///     Initializes a stock code.
        /// </summary>
        /// <param name="code">The stock code.</param>
        private Sku(string code)
        {
            Code = code;
        }

        /// <summary>
        ///     Gets the stock code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        ///     Parses a stock code, accepting three-letter uppercase codes only.
        /// </summary>
        /// <param name="code">The code to parse.</param>
        /// <param name="sku">The parsed stock code.</param>
        public static bool TryParse(string code, out Sku sku)
        {
            if (code is { Length: 3 } && code.All(static character => character is >= 'A' and <= 'Z'))
            {
                sku = new Sku(code);

                return true;
            }

            sku = default;

            return false;
        }
    }

    /// <summary>
    ///     A quantity created through TryCreate with a positive guard.
    /// </summary>
    private readonly record struct Quantity
    {
        /// <summary>
        ///     Initializes a quantity.
        /// </summary>
        /// <param name="value">The positive quantity.</param>
        private Quantity(int value)
        {
            Value = value;
        }

        /// <summary>
        ///     Gets the positive quantity.
        /// </summary>
        public int Value { get; }

        /// <summary>
        ///     Creates a quantity, accepting positive values only.
        /// </summary>
        /// <param name="value">The quantity value.</param>
        /// <param name="quantity">The created quantity.</param>
        public static bool TryCreate(int value, out Quantity quantity)
        {
            if (value > 0)
            {
                quantity = new Quantity(value);

                return true;
            }

            quantity = default;

            return false;
        }
    }

    /// <summary>
    ///     A tracking identifier created through a Create factory.
    /// </summary>
    private sealed class TrackingId
    {
        /// <summary>
        ///     Initializes a tracking identifier.
        /// </summary>
        /// <param name="value">The identifier value.</param>
        private TrackingId(Guid value)
        {
            Value = value;
        }

        /// <summary>
        ///     Gets the identifier value.
        /// </summary>
        public Guid Value { get; }

        /// <summary>
        ///     Creates a tracking identifier, rejecting the empty value.
        /// </summary>
        /// <param name="value">The identifier value.</param>
        public static TrackingId Create(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("The identifier must not be empty.", nameof(value));
            }

            return new TrackingId(value);
        }
    }

    /// <summary>
    ///     A currency code created through a From factory with a three-letter guard.
    /// </summary>
    private readonly record struct Currency
    {
        /// <summary>
        ///     Initializes a currency code.
        /// </summary>
        /// <param name="code">The currency code.</param>
        private Currency(string code)
        {
            Code = code;
        }

        /// <summary>
        ///     Gets the currency code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        ///     Creates a currency code, accepting three-letter codes only.
        /// </summary>
        /// <param name="code">The currency code.</param>
        public static Currency From(string code)
        {
            if (code is not { Length: 3 })
            {
                throw new ArgumentException("The currency code must be three letters.", nameof(code));
            }

            return new Currency(code);
        }
    }

    /// <summary>
    ///     A slug parsed through a Parse factory with a lowercase guard.
    /// </summary>
    private sealed class Slug
    {
        /// <summary>
        ///     Initializes a slug.
        /// </summary>
        /// <param name="value">The slug value.</param>
        private Slug(string value)
        {
            Value = value;
        }

        /// <summary>
        ///     Gets the slug value.
        /// </summary>
        public string Value { get; }

        /// <summary>
        ///     Parses a slug, accepting non-empty lowercase values only.
        /// </summary>
        /// <param name="value">The value to parse.</param>
        public static Slug Parse(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.All(static character => character is >= 'a' and <= 'z'))
            {
                throw new ArgumentException("The slug must be non-empty lowercase text.", nameof(value));
            }

            return new Slug(value);
        }
    }

    /// <summary>
    ///     An API key created through a parameterless factory.
    /// </summary>
    private sealed class ApiKey
    {
        /// <summary>
        ///     Initializes an API key.
        /// </summary>
        /// <param name="value">The key value.</param>
        private ApiKey(string value)
        {
            Value = value;
        }

        /// <summary>
        ///     Gets the key value.
        /// </summary>
        public string Value { get; }

        /// <summary>
        ///     Creates the sample API key.
        /// </summary>
        public static ApiKey Create()
        {
            return new ApiKey("key-sample");
        }
    }

    /// <summary>
    ///     A country code guarded without naming the parameter.
    /// </summary>
    private sealed record Country
    {
        /// <summary>
        ///     Initializes a country code, rejecting codes that are not two letters.
        /// </summary>
        /// <param name="code">The two-letter country code.</param>
        public Country(string code)
        {
            if (code is not { Length: 2 })
            {
                throw new ArgumentException("The country code must be two letters.");
            }

            Code = code;
        }

        /// <summary>
        ///     Gets the two-letter country code.
        /// </summary>
        public string Code { get; }
    }

    /// <summary>
    ///     A positional record composing identifier, money, and closed-set members.
    /// </summary>
    /// <param name="Id">The order identifier.</param>
    /// <param name="Price">The order price.</param>
    /// <param name="Status">The order status.</param>
    private sealed record PlaceOrder(OrderId Id, Money Price, OrderStatus Status);

    /// <summary>
    ///     A plain struct with no explicit constructor.
    /// </summary>
    private struct LinePoint
    {
        /// <summary>
        ///     Gets or sets the horizontal coordinate.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        ///     Gets or sets the vertical label.
        /// </summary>
        public string Y { get; set; }
    }

    /// <summary>
    ///     Names the member path and type a record cannot sample.
    /// </summary>
    [Fact]
    public async Task DescribeFailure_TypeWithUnsampleableMember_ShouldNamePathAndType()
    {
        var factory = new DefaultMessageSampleFactory();

        var failure = await factory.DescribeFailureAsync(typeof(Crate)).ConfigureAwait(false);

        failure.Should().NotBeNull();
        failure!.FailedType.Should().Be<SealedBox>();
        failure.MemberPath.Should().Be(nameof(Crate.Box));
        failure.Reason.Should().Contain("no static factory or constructor accepts sample values");
    }

    /// <summary>
    ///     Diagnoses an unsampleable root with an empty member path.
    /// </summary>
    [Fact]
    public async Task DescribeFailure_UnsampleableRoot_ShouldUseEmptyPath()
    {
        var factory = new DefaultMessageSampleFactory();

        var failure = await factory.DescribeFailureAsync(typeof(SealedBox)).ConfigureAwait(false);

        failure.Should().NotBeNull();
        failure!.FailedType.Should().Be<SealedBox>();
        failure.MemberPath.Should().BeEmpty();
    }

    /// <summary>
    ///     Answers no failure for a type the factory samples.
    /// </summary>
    [Fact]
    public async Task DescribeFailure_SampleableType_ShouldReturnNull()
    {
        var factory = new DefaultMessageSampleFactory();

        var failure = await factory.DescribeFailureAsync(typeof(OrderId)).ConfigureAwait(false);

        failure.Should().BeNull();
    }

    /// <summary>
    ///     Refuses interfaces with a dedicated reason.
    /// </summary>
    [Fact]
    public async Task DescribeFailure_Interface_ShouldRefuseInterfaces()
    {
        var factory = new DefaultMessageSampleFactory();

        var sample = await factory.CreateSampleAsync(typeof(IDisposable)).ConfigureAwait(false);
        var failure = await factory.DescribeFailureAsync(typeof(IDisposable)).ConfigureAwait(false);

        sample.Should().BeNull();
        failure.Should().NotBeNull();
        failure!.Reason.Should().Contain("interfaces cannot be sampled");
    }

    /// <summary>
    ///     Builds a registered sample for the exact type.
    /// </summary>
    [Fact]
    public async Task CreateSample_WithRegisteredSample_ShouldUseIt()
    {
        var factory = new DefaultMessageSampleFactory(new Dictionary<Type, Func<object?>>
        {
            [typeof(SealedBox)] = () => new SealedBox(500),
        });

        var sample = await factory.CreateSampleAsync(typeof(SealedBox)).ConfigureAwait(false);

        var box = sample.Should().BeOfType<SealedBox>().Subject;
        box.Code.Should().Be(500);
    }

    /// <summary>
    ///     Builds a registered sample for a nested member type.
    /// </summary>
    [Fact]
    public async Task CreateSample_WithRegisteredNestedSample_ShouldUseIt()
    {
        var factory = new DefaultMessageSampleFactory(new Dictionary<Type, Func<object?>>
        {
            [typeof(SealedBox)] = () => new SealedBox(500),
        });

        var sample = await factory.CreateSampleAsync(typeof(Crate)).ConfigureAwait(false);

        var crate = sample.Should().BeOfType<Crate>().Subject;
        crate.Box.Code.Should().Be(500);
    }

    /// <summary>
    ///     Diagnoses a registered sample that answers nothing.
    /// </summary>
    [Fact]
    public async Task CreateSample_WithNullRegisteredSample_ShouldDiagnose()
    {
        var factory = new DefaultMessageSampleFactory(new Dictionary<Type, Func<object?>>
        {
            [typeof(SealedBox)] = () => null,
        });

        var sample = await factory.CreateSampleAsync(typeof(SealedBox)).ConfigureAwait(false);
        var failure = await factory.DescribeFailureAsync(typeof(SealedBox)).ConfigureAwait(false);

        sample.Should().BeNull();
        failure.Should().NotBeNull();
        failure!.Reason.Should().Contain("the registered sample returned no value");
    }

    /// <summary>
    ///     Diagnoses a registered sample that answers the wrong type.
    /// </summary>
    [Fact]
    public async Task CreateSample_WithMismatchedRegisteredSample_ShouldDiagnose()
    {
        var factory = new DefaultMessageSampleFactory(new Dictionary<Type, Func<object?>>
        {
            [typeof(SealedBox)] = () => new LinePoint(),
        });

        var sample = await factory.CreateSampleAsync(typeof(SealedBox)).ConfigureAwait(false);
        var failure = await factory.DescribeFailureAsync(typeof(SealedBox)).ConfigureAwait(false);

        sample.Should().BeNull();
        failure.Should().NotBeNull();
        failure!.Reason.Should().Contain("not assignable");
    }

    /// <summary>
    ///     Diagnoses a registered sample that throws.
    /// </summary>
    [Fact]
    public async Task CreateSample_WithThrowingRegisteredSample_ShouldDiagnose()
    {
        var factory = new DefaultMessageSampleFactory(new Dictionary<Type, Func<object?>>
        {
            [typeof(SealedBox)] = () => throw new InvalidOperationException("The vault is sealed."),
        });

        var sample = await factory.CreateSampleAsync(typeof(SealedBox)).ConfigureAwait(false);
        var failure = await factory.DescribeFailureAsync(typeof(SealedBox)).ConfigureAwait(false);

        sample.Should().BeNull();
        failure.Should().NotBeNull();
        failure!.Reason.Should().Contain("the registered sample threw");
        failure.Reason.Should().Contain("The vault is sealed.");
    }

    /// <summary>
    ///     A box guarded beyond any general rule, sampled only when registered.
    /// </summary>
    private sealed class SealedBox
    {
        /// <summary>
        ///     Initializes a box, accepting codes above one hundred only.
        /// </summary>
        /// <param name="code">The box code.</param>
        public SealedBox(int code)
        {
            if (code <= 100)
            {
                throw new ArgumentException("The code must exceed one hundred.", nameof(code));
            }

            Code = code;
        }

        /// <summary>
        ///     Gets the box code.
        /// </summary>
        public int Code { get; }
    }

    /// <summary>
    ///     A record holding a box the default factory cannot sample.
    /// </summary>
    /// <param name="Box">The box.</param>
    private sealed record Crate(SealedBox Box);
}
