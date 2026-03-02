using DataverseFieldUpdateTracker.Rag;
using Xunit;

namespace DataverseFieldUpdateTracker.Tests.Rag;

/// <summary>
/// Tests for the static JavaScript-parsing helpers in WebResourceRag.
/// All patterns ported from webresource_rag.py – tests verify exact parity.
/// </summary>
public sealed class WebResourceRagStaticTests
{
    // ── ExtractJavaScriptActions ──────────────────────────────────────────────

    [Fact]
    public void ExtractJavaScriptActions_SetValuePresent_ReturnsSetValue()
    {
        var js = "formContext.getAttribute('name').setValue('test');";
        var actions = WebResourceRag.ExtractJavaScriptActions(js);
        Assert.Contains("SET_VALUE", actions);
    }

    [Fact]
    public void ExtractJavaScriptActions_NoKeywords_ReturnsEmpty()
    {
        var js = "console.log('hello world');";
        var actions = WebResourceRag.ExtractJavaScriptActions(js);
        Assert.Empty(actions);
    }

    // ── ExtractFieldsModified – Pattern 1: formContext.getAttribute ───────────

    [Fact]
    public void ExtractFieldsModified_Pattern1_DirectChain_ReturnsField()
    {
        var js = "formContext.getAttribute(\"emailaddress1\").setValue(\"test@example.com\");";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Contains("emailaddress1", fields);
    }

    [Fact]
    public void ExtractFieldsModified_Pattern1_SingleQuotes_ReturnsField()
    {
        var js = "formContext.getAttribute('name').setValue('Contoso');";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Contains("name", fields);
    }

    [Fact]
    public void ExtractFieldsModified_Pattern1_WithSpaces_ReturnsField()
    {
        // Optional whitespace between method calls
        var js = "formContext. getAttribute( 'telephone1' ). setValue( '555' );";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Contains("telephone1", fields);
    }

    // ── Pattern 2: formContext.getControl ────────────────────────────────────

    [Fact]
    public void ExtractFieldsModified_Pattern2_GetControl_ReturnsField()
    {
        var js = "formContext.getControl('statuscode').setValue(1);";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Contains("statuscode", fields);
    }

    // ── Pattern 3: Xrm.Page (deprecated) ─────────────────────────────────────

    [Fact]
    public void ExtractFieldsModified_Pattern3_XrmPage_ReturnsField()
    {
        var js = "Xrm.Page.getAttribute('address1_city').setValue('London');";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Contains("address1_city", fields);
    }

    // ── Pattern 4: Variable assignment ───────────────────────────────────────

    [Fact]
    public void ExtractFieldsModified_Pattern4_VariableAssignment_ReturnsField()
    {
        var js =
            "var ctrl = formContext.getAttribute('revenue');\n" +
            "ctrl.setValue(500000);";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Contains("revenue", fields);
    }

    [Fact]
    public void ExtractFieldsModified_Pattern4_LetDeclaration_ReturnsField()
    {
        var js =
            "let attr = formContext.getAttribute('industrycode');\n" +
            "attr.setValue(3);";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Contains("industrycode", fields);
    }

    [Fact]
    public void ExtractFieldsModified_Pattern4_VariableAssignedButNotUsed_ReturnsNothing()
    {
        // Variable is assigned but .setValue() never called on it
        var js = "var ctrl = formContext.getAttribute('name');";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.DoesNotContain("name", fields);
    }

    // ── Pattern 5: executionContext.getFormContext ────────────────────────────

    [Fact]
    public void ExtractFieldsModified_Pattern5_ExecutionContext_ReturnsField()
    {
        var js = "executionContext.getFormContext().getAttribute('creditlimit').setValue(0);";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Contains("creditlimit", fields);
    }

    // ── Deduplication ─────────────────────────────────────────────────────────

    [Fact]
    public void ExtractFieldsModified_DuplicateField_ReturnedOnce()
    {
        var js =
            "formContext.getAttribute('name').setValue('A');\n" +
            "formContext.getAttribute('name').setValue('B');";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Single(fields.Where(f => f == "name"));
    }

    // ── Case sensitivity ──────────────────────────────────────────────────────

    [Fact]
    public void ExtractFieldsModified_IsCaseSensitive()
    {
        var js = "formContext.getAttribute('Name').setValue('test');";
        var fields = WebResourceRag.ExtractFieldsModified(js);
        // "Name" (capital N) should appear; "name" should NOT (different field)
        Assert.Contains("Name", fields);
        Assert.DoesNotContain("name", fields);
    }

    // ── Multiple fields in one file ───────────────────────────────────────────

    [Fact]
    public void ExtractFieldsModified_MultipleFields_ReturnsAll()
    {
        var js =
            "formContext.getAttribute('emailaddress1').setValue(email);\n" +
            "formContext.getAttribute('telephone1').setValue(phone);\n" +
            "Xrm.Page.getAttribute('address1_city').setValue(city);";

        var fields = WebResourceRag.ExtractFieldsModified(js);
        Assert.Contains("emailaddress1", fields);
        Assert.Contains("telephone1",    fields);
        Assert.Contains("address1_city", fields);
    }

    // ── Empty / no-op code ────────────────────────────────────────────────────

    [Fact]
    public void ExtractFieldsModified_EmptyString_ReturnsEmpty()
    {
        var fields = WebResourceRag.ExtractFieldsModified(string.Empty);
        Assert.Empty(fields);
    }
}
