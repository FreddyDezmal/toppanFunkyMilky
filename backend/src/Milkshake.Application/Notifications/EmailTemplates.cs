using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Payments;

namespace Milkshake.Application.Notifications;

/// <summary>A rendered email: subject, HTML and plain-text versions.</summary>
public sealed record RenderedEmail(string Subject, string Html, string Text);

/// <summary>
/// Template version 1 of the five transactional emails (decision D2). Every value comes from the stored payload and is
/// HTML-encoded; the only links go to the order page on our own site (no tokens, no one-click actions, no tracking).
/// Pending refunds are never described as completed.
/// </summary>
public static class EmailTemplates
{
    private const string Brand = "Milky Shaky Custom Drinks";

    public static RenderedEmail Render(EmailNotification notification, Uri publicBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(publicBaseUrl);

        if (notification.TemplateVersion != EmailPayloads.CurrentTemplateVersion)
        {
            throw new EmailRenderException($"Template version {notification.TemplateVersion} is not supported.");
        }

        try
        {
            string name = notification.RecipientName;
            return notification.Kind switch
            {
                NotificationKind.OrderConfirmed => OrderConfirmed(EmailPayloads.Deserialize<OrderConfirmedPayload>(notification.Payload), name, publicBaseUrl),
                NotificationKind.CancellationRequested =>
                    CancellationRequested(EmailPayloads.Deserialize<CancellationRequestedPayload>(notification.Payload), name, publicBaseUrl),
                NotificationKind.OrderCancelled => OrderCancelled(EmailPayloads.Deserialize<OrderCancelledPayload>(notification.Payload), name, publicBaseUrl),
                NotificationKind.RefundProcessed => RefundProcessed(EmailPayloads.Deserialize<RefundOutcomePayload>(notification.Payload), name, publicBaseUrl),
                NotificationKind.RefundFailed => RefundFailed(EmailPayloads.Deserialize<RefundOutcomePayload>(notification.Payload), name, publicBaseUrl),
                _ => throw new EmailRenderException($"No template for {notification.Kind}."),
            };
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new EmailRenderException("The email payload could not be read.", exception);
        }
    }

    /// <summary>The order page on our own site; the reference is escaped, so nothing in it can change the link.</summary>
    public static Uri OrderLink(Uri publicBaseUrl, string orderReference)
    {
        ArgumentNullException.ThrowIfNull(publicBaseUrl);
        return new Uri(publicBaseUrl, $"orders/{Uri.EscapeDataString(orderReference)}");
    }

    private static RenderedEmail OrderConfirmed(OrderConfirmedPayload p, string name, Uri site)
    {
        var f = new Formatter(p.Locale, p.CurrencyCode);
        var email = new Email(name);
        email.Paragraph("Thank you for your order. Your payment has been received and your order is confirmed.");
        email.Rows("Your order",
        [
            ("Order number", p.OrderReference),
            ("Status", "Confirmed and paid"),
            ("Pickup", p.RestaurantName),
            .. p.RestaurantAddress is { Length: > 0 } address ? [("Address", address)] : Array.Empty<(string, string)>(),
            ("Pickup time", f.Time(p.PickupAtUtc, p.PickupTimeZone)),
        ]);
        email.Drinks(p.Drinks.Select(d => (
            $"Milkshake {d.Number}",
            new[] { $"{d.Flavour} ({f.Money(d.FlavourFee)})", $"{d.Consistency} ({f.Money(d.ConsistencyFee)})", $"{d.Topping} ({f.Money(d.ToppingFee)})" },
            f.Money(d.Price))).ToList());
        email.Rows("Receipt",
        [
            ("Subtotal", f.Money(p.Subtotal)),
            ("Discount", p.Discount > 0 ? $"− {f.Money(p.Discount)}" : "None"),
            ($"VAT ({f.Percent(p.VatRate)})", f.Money(p.Vat)),
            ("Total paid", f.Money(p.Total)),
            ("Payment reference", p.PaymentReference),
            ("Paid on", f.Time(p.PaidAt, p.PickupTimeZone)),
        ], emphasiseRow: 3);
        email.Link("View your order", OrderLink(site, p.OrderReference));
        return email.Build($"Order {p.OrderReference} confirmed – your receipt", p.OrderReference);
    }

    private static RenderedEmail CancellationRequested(CancellationRequestedPayload p, string name, Uri site)
    {
        var f = new Formatter(p.Locale, p.CurrencyCode);
        var email = new Email(name);
        email.Paragraph($"The restaurant has asked to cancel your order {p.OrderReference}.");
        email.Quote("Reason", p.Reason);
        email.Paragraph(p.IsPaid
            ? $"If you accept, your order is cancelled and you get back everything you paid ({f.Money(p.RefundAmount)}), with no fee."
            : "If you accept, your order is cancelled. You have not paid for it, so nothing is charged.");
        email.Paragraph("If you decline, your order stays as it is.");
        email.Paragraph($"Please answer before the pickup time, {f.Time(p.PickupAtUtc, p.PickupTimeZone)}. " +
                        "If you do not answer by then, the request lapses and your order stays as it is.");
        email.Paragraph("To answer, sign in and open your order. For your security this email cannot accept or decline the request itself.");
        email.Link("Open your order", OrderLink(site, p.OrderReference));
        return email.Build($"Order {p.OrderReference}: the restaurant has asked to cancel", p.OrderReference);
    }

    private static RenderedEmail OrderCancelled(OrderCancelledPayload p, string name, Uri site)
    {
        var f = new Formatter(p.Locale, p.CurrencyCode);
        var email = new Email(name);
        email.Paragraph(p.AtRestaurantRequest
            ? $"Your order {p.OrderReference} has been cancelled, as you agreed to the restaurant's request."
            : $"Your order {p.OrderReference} has been cancelled.");

        if (!p.WasPaid)
        {
            email.Paragraph("You had not paid for this order, so nothing was charged.");
        }
        else
        {
            var rows = new List<(string, string)> { ("Amount paid", f.Money(p.PaidAmount)) };
            if (p.Fee > 0)
            {
                rows.Add(("Cancellation fee kept", f.Money(p.Fee)));
            }

            rows.Add(("Refund", f.Money(p.RefundAmount)));
            rows.Add(("Refund status", "On its way – not yet completed"));
            if (p.RefundReference is { Length: > 0 } reference)
            {
                rows.Add(("Refund reference", reference));
            }

            email.Rows("Refund", rows, emphasiseRow: rows.FindIndex(r => r.Item1 == "Refund"));
            email.Paragraph("Refunds can take several business days to reach your card. We will email you when the refund is complete.");
        }

        email.Link("View your order", OrderLink(site, p.OrderReference));
        return email.Build($"Order {p.OrderReference} has been cancelled", p.OrderReference);
    }

    private static RenderedEmail RefundProcessed(RefundOutcomePayload p, string name, Uri site)
    {
        var f = new Formatter(p.Locale, p.CurrencyCode);
        var email = new Email(name);
        email.Paragraph(p.Kind == RefundKind.UnacceptedPayment
            ? $"We have refunded a payment for order {p.OrderReference} that we could not accept. Our payment provider has confirmed the refund."
            : $"Your refund for order {p.OrderReference} is complete: our payment provider has confirmed it.");
        var rows = new List<(string, string)> { ("Amount refunded", f.Money(p.Amount)) };
        if (p.Fee > 0)
        {
            rows.Add(("Cancellation fee kept", f.Money(p.Fee)));
        }

        rows.Add(("Refund reference", p.RefundReference));
        rows.Add(("Completed on", f.Time(p.CompletedAt, p.TimeZone)));
        email.Rows("Refund", rows, emphasiseRow: 0);
        email.Paragraph("It may take a few days to appear on your card statement.");
        email.Link("View your order", OrderLink(site, p.OrderReference));
        return email.Build($"Refund for order {p.OrderReference} completed", p.OrderReference);
    }

    private static RenderedEmail RefundFailed(RefundOutcomePayload p, string name, Uri site)
    {
        var f = new Formatter(p.Locale, p.CurrencyCode);
        var email = new Email(name);
        email.Paragraph($"We could not complete your refund for order {p.OrderReference} automatically.");
        email.Rows("Refund", [("Amount", f.Money(p.Amount)), ("Refund reference", p.RefundReference), ("Status", "Not completed")]);
        email.Paragraph("Please contact the restaurant and quote the refund reference, so that we can arrange your refund.");
        email.Paragraph("We will never ask for your card or bank details by email.");
        email.Link("View your order", OrderLink(site, p.OrderReference));
        return email.Build($"Refund for order {p.OrderReference}: please contact us", p.OrderReference);
    }

    /// <summary>Amounts, percentages and times as the app shows them for the stored display locale.</summary>
    private sealed class Formatter(string locale, string currencyCode)
    {
        private readonly CultureInfo _culture = CultureOf(locale);

        public string Money(decimal amount)
        {
            bool localCurrency;
            try
            {
                localCurrency = new RegionInfo(_culture.Name).ISOCurrencySymbol == currencyCode;
            }
            catch (ArgumentException)
            {
                localCurrency = false;
            }

            return localCurrency
                ? amount.ToString("C", _culture)
                : $"{currencyCode} {amount.ToString("N2", _culture)}";
        }

        public string Percent(decimal rate) => (rate * 100m).ToString("0.##", _culture) + "%";

        public string Time(DateTimeOffset utc, string timeZone)
        {
            string pattern = "dddd d MMMM yyyy, HH:mm";
            return TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out TimeZoneInfo? zone)
                ? TimeZoneInfo.ConvertTime(utc, zone).ToString(pattern, _culture)
                : utc.ToUniversalTime().ToString(pattern, _culture) + " (UTC)";
        }

        private static CultureInfo CultureOf(string name)
        {
            try
            {
                return CultureInfo.GetCultureInfo(name);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }

    /// <summary>
    /// Builds the HTML and plain-text versions side by side. HTML: inline styles and a 600-pixel table layout (works in
    /// common mail clients), no images, scripts or tracking. Every value is HTML-encoded.
    /// </summary>
    private sealed class Email
    {
        private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;
        private readonly StringBuilder _html = new();
        private readonly StringBuilder _text = new();

        public Email(string recipientName)
        {
            string greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hello," : $"Hi {recipientName},";
            Paragraph(greeting);
        }

        public void Paragraph(string text)
        {
            _html.Append("<p style=\"margin:0 0 16px;font-size:16px;line-height:24px;color:#1e293b;\">").Append(E(text)).Append("</p>");
            _text.AppendLine(text).AppendLine();
        }

        public void Quote(string label, string text)
        {
            _html.Append("<p style=\"margin:0 0 16px;padding:12px 16px;background:#fffbeb;border-left:4px solid #f59e0b;font-size:16px;line-height:24px;color:#78350f;\">")
                .Append("<strong>").Append(E(label)).Append(":</strong> “").Append(E(text)).Append("”</p>");
            _text.Append(label).Append(": \"").Append(text).AppendLine("\"").AppendLine();
        }

        public void Rows(string heading, IReadOnlyList<(string Label, string Value)> rows, int emphasiseRow = -1)
        {
            Heading(heading);
            _html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:0 0 20px;border-collapse:collapse;\">");
            for (int i = 0; i < rows.Count; i++)
            {
                string weight = i == emphasiseRow ? "font-weight:bold;" : string.Empty;
                _html.Append("<tr><td style=\"padding:6px 0;border-bottom:1px solid #e2e8f0;font-size:15px;color:#475569;").Append(weight).Append("\">")
                    .Append(E(rows[i].Label))
                    .Append("</td><td align=\"right\" style=\"padding:6px 0;border-bottom:1px solid #e2e8f0;font-size:15px;color:#0f172a;").Append(weight).Append("\">")
                    .Append(E(rows[i].Value)).Append("</td></tr>");
                _text.Append(rows[i].Label).Append(": ").AppendLine(rows[i].Value);
            }

            _html.Append("</table>");
            _text.AppendLine();
        }

        public void Drinks(IReadOnlyList<(string Title, string[] Choices, string Price)> drinks)
        {
            Heading(drinks.Count == 1 ? "1 milkshake" : $"{drinks.Count} milkshakes");
            _html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:0 0 20px;border-collapse:collapse;\">");
            foreach ((string title, string[] choices, string price) in drinks)
            {
                _html.Append("<tr><td style=\"padding:8px 0;border-bottom:1px solid #e2e8f0;font-size:15px;color:#0f172a;\"><strong>").Append(E(title))
                    .Append("</strong><br><span style=\"color:#475569;\">").Append(string.Join("<br>", choices.Select(E))).Append("</span></td>")
                    .Append("<td align=\"right\" valign=\"top\" style=\"padding:8px 0;border-bottom:1px solid #e2e8f0;font-size:15px;color:#0f172a;\">")
                    .Append(E(price)).Append("</td></tr>");
                _text.Append(title).Append(" – ").AppendLine(price);
                foreach (string choice in choices)
                {
                    _text.Append("  ").AppendLine(choice);
                }
            }

            _html.Append("</table>");
            _text.AppendLine();
        }

        public void Link(string label, Uri href)
        {
            _html.Append("<p style=\"margin:8px 0 24px;\"><a href=\"").Append(E(href.AbsoluteUri))
                .Append("\" style=\"display:inline-block;padding:12px 20px;background:#db2777;color:#ffffff;text-decoration:none;border-radius:8px;font-size:16px;font-weight:bold;\">")
                .Append(E(label)).Append("</a></p>");
            _text.Append(label).Append(": ").AppendLine(href.AbsoluteUri).AppendLine();
        }

        public RenderedEmail Build(string subject, string orderReference)
        {
            string footer = $"{Brand} · You received this email about your order {orderReference}.";
            string html =
                "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                "<title>" + E(subject) + "</title></head>" +
                "<body style=\"margin:0;padding:0;background:#fdf2f8;font-family:Arial,Helvetica,sans-serif;\">" +
                "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#fdf2f8;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">" +
                "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;background:#ffffff;border-radius:12px;\">" +
                "<tr><td style=\"padding:24px 28px 8px;font-size:22px;font-weight:bold;color:#db2777;\">" + E(Brand) + "</td></tr>" +
                "<tr><td style=\"padding:8px 28px 8px;\">" + _html + "</td></tr>" +
                "<tr><td style=\"padding:16px 28px 24px;font-size:13px;line-height:20px;color:#64748b;border-top:1px solid #e2e8f0;\">" + E(footer) + "</td></tr>" +
                "</table></td></tr></table></body></html>";
            string text = _text.Append("— ").AppendLine(footer).ToString();
            return new RenderedEmail(Sanitise(subject), html, text);
        }

        private void Heading(string text)
        {
            _html.Append("<h2 style=\"margin:8px 0 8px;font-size:18px;color:#0f172a;\">").Append(E(text)).Append("</h2>");
            _text.AppendLine(text.ToUpperInvariant());
        }

        private static string E(string value) => Encoder.Encode(value);

        private static string Sanitise(string subject) => new(subject.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }
}
