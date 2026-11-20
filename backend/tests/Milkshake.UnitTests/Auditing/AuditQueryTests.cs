using System.Text.Json;
using Milkshake.Application.Management;
using Milkshake.Domain.Auditing;

namespace Milkshake.UnitTests.Auditing;

/// <summary>M11 audit query (AC-11.4): strict filters, opaque paging, and personal data masked on output.</summary>
public class AuditQueryTests
{
    private static Dictionary<string, string[]> Query(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => new[] { p.Value });

    // ---------- filters ----------

    [Fact]
    public void No_filters_means_the_newest_fifty_changes()
    {
        var errors = new Dictionary<string, string>();

        AuditQuery query = AuditQueryParser.ParseChanges(Query(), errors)!;

        Assert.Empty(errors);
        Assert.Equal(new AuditQuery(null, null, null, null, null, null, null, null, 50), query);
    }

    [Fact]
    public void Every_change_filter_is_read()
    {
        var errors = new Dictionary<string, string>();
        string cursor = new AuditCursor(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), 42).Encode();

        AuditQuery query = AuditQueryParser.ParseChanges(Query(
            ("from", "2026-11-01T00:00:00Z"), ("to", "2026-11-20T10:00:00+02:00"), ("user", " manager@example.test "),
            ("actor", "system:email-dispatcher"), ("entity", "orders"), ("action", "update"), ("q", "Cancelled"),
            ("cursor", cursor), ("limit", "25")), errors)!;

        Assert.Empty(errors);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), query.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), query.To);
        Assert.Equal(("manager@example.test", "system:email-dispatcher", "orders", AuditAction.Update, "Cancelled", 25),
            (query.User, query.Actor, query.Entity, query.Action, query.Text, query.Limit));
        Assert.Equal(42, query.After!.Id);
    }

    [Theory]
    [InlineData("limit", "0")]
    [InlineData("limit", "101")]
    [InlineData("limit", "ten")]
    [InlineData("limit", "-5")]
    [InlineData("from", "yesterday")]
    [InlineData("from", "1900-01-01T00:00:00Z")]
    [InlineData("action", "Truncate")]
    [InlineData("action", "1")]
    [InlineData("actor", "root")]
    [InlineData("entity", "Orders")]
    [InlineData("entity", "orders; drop table orders")]
    [InlineData("cursor", "not-a-cursor")]
    [InlineData("password", "x")]
    public void An_invalid_change_filter_is_refused(string key, string value)
    {
        var errors = new Dictionary<string, string>();

        Assert.Null(AuditQueryParser.ParseChanges(Query((key, value)), errors));
        Assert.True(errors.ContainsKey(key));
    }

    [Fact]
    public void Free_text_and_user_are_bounded_and_single_line()
    {
        var errors = new Dictionary<string, string>();

        Assert.Null(AuditQueryParser.ParseChanges(Query(("q", new string('x', 101)), ("user", "a\nb")), errors));
        Assert.True(errors.ContainsKey("q"));
        Assert.True(errors.ContainsKey("user"));
    }

    [Fact]
    public void A_filter_given_twice_is_refused()
    {
        var errors = new Dictionary<string, string>();

        Assert.Null(AuditQueryParser.ParseChanges(new Dictionary<string, string[]> { ["entity"] = ["orders", "payments"] }, errors));
        Assert.True(errors.ContainsKey("entity"));
    }

    [Theory]
    [InlineData("2026-11-20T00:00:00Z", "2026-11-20T00:00:00Z")]
    [InlineData("2026-11-20T00:00:00Z", "2026-11-19T00:00:00Z")]
    [InlineData("2025-01-01T00:00:00Z", "2026-01-03T00:00:00Z")]
    public void A_period_must_run_forwards_for_at_most_366_days(string from, string to)
    {
        var errors = new Dictionary<string, string>();

        Assert.Null(AuditQueryParser.ParseChanges(Query(("from", from), ("to", to)), errors));
        Assert.True(errors.ContainsKey("to"));
    }

    [Fact]
    public void Security_filters_are_read_and_change_filters_are_not_accepted_there()
    {
        var errors = new Dictionary<string, string>();
        SecurityEventQuery query = AuditQueryParser.ParseSecurity(Query(("eventType", "loginfailed"), ("limit", "10")), errors)!;
        Assert.Equal((SecurityEventType.LoginFailed, 10), (query.EventType, query.Limit));

        var refused = new Dictionary<string, string>();
        Assert.Null(AuditQueryParser.ParseSecurity(Query(("entity", "orders")), refused));
        Assert.True(refused.ContainsKey("entity"));
    }

    // ---------- cursor ----------

    [Fact]
    public void A_cursor_round_trips_and_is_url_safe()
    {
        var position = new AuditCursor(new DateTimeOffset(2026, 11, 20, 8, 30, 15, 123, TimeSpan.Zero).AddTicks(4567), -1_000_042);

        string encoded = position.Encode();

        Assert.Matches("^[A-Za-z0-9_-]+$", encoded);
        Assert.Equal(position, AuditCursor.TryDecode(encoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%%%")]
    [InlineData("MTIzNA")]
    [InlineData("YWJjOjEy")]
    public void A_damaged_cursor_is_not_accepted(string value)
    {
        Assert.Null(AuditCursor.TryDecode(value));
    }

    // ---------- masking ----------

    [Theory]
    [InlineData("thandi@example.test", "t***@example.test")]
    [InlineData("x@y.z", "x***@y.z")]
    [InlineData("not-an-address", "***")]
    [InlineData("@example.test", "***")]
    [InlineData("a@b@c", "***")]
    public void Email_addresses_keep_only_the_first_letter_and_domain(string email, string expected)
    {
        Assert.Equal(expected, AuditMasking.MaskEmail(email));
    }

    [Theory]
    [InlineData("196.21.4.7", "196.21.x.x")]
    [InlineData("::ffff:196.21.4.7", "196.21.x.x")]
    [InlineData("2001:db8:85a3::8a2e:370:7334", "2001:db8:…")]
    [InlineData("not-an-ip", "***")]
    public void Ip_addresses_keep_only_the_network(string ip, string expected)
    {
        Assert.Equal(expected, AuditMasking.MaskIp(ip));
    }

    [Fact]
    public void Account_details_are_masked_but_other_fields_are_shown()
    {
        string json = """[{"field":"email","old":null,"new":"thandi@example.test"},{"field":"first_name","old":"T","new":"Thandi"},{"field":"lockout_end","old":null,"new":"2026-11-20T08:00:00+00:00"}]""";

        IReadOnlyList<AuditFieldChange> changes = AuditMasking.Changes("users", json);

        Assert.Equal(["email", "first_name", "lockout_end"], changes.Select(c => c.Field).ToArray());
        Assert.Null(changes[0].Old);
        Assert.Equal("***", changes[0].New!.Value.GetString());
        Assert.Equal(("***", "***"), (changes[1].Old!.Value.GetString(), changes[1].New!.Value.GetString()));
        Assert.Equal("2026-11-20T08:00:00+00:00", changes[2].New!.Value.GetString());
    }

    [Fact]
    public void Other_tables_show_their_values_with_their_types()
    {
        string json = """[{"field":"status","old":"PendingPayment","new":"Confirmed"},{"field":"total","old":null,"new":29.90}]""";

        IReadOnlyList<AuditFieldChange> changes = AuditMasking.Changes("orders", json);

        Assert.Equal("Confirmed", changes[0].New!.Value.GetString());
        Assert.Equal(JsonValueKind.Number, changes[1].New!.Value.ValueKind);
        Assert.Equal(29.90m, changes[1].New!.Value.GetDecimal());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"field\":\"x\"}")]
    public void Unreadable_content_is_never_shown_raw(string json)
    {
        AuditFieldChange only = Assert.Single(AuditMasking.Changes("orders", json));

        Assert.Equal(("(unreadable)", "***"), (only.Field, only.New!.Value.GetString()));
    }

    [Fact]
    public void Personal_tables_are_known()
    {
        Assert.Contains("users", AuditMasking.PersonalTables);
        Assert.DoesNotContain("orders", AuditMasking.PersonalTables);
    }
}
