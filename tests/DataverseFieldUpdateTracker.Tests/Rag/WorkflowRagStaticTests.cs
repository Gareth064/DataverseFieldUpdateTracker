using DataverseFieldUpdateTracker.Rag;
using Xunit;

namespace DataverseFieldUpdateTracker.Tests.Rag;

/// <summary>
/// Tests for the static XAML-parsing helpers in WorkflowRag.
/// These are pure functions (no Gemini/network calls) and are fully deterministic.
/// </summary>
public sealed class WorkflowRagStaticTests
{
    // ── ExtractXamlActions ────────────────────────────────────────────────────

    [Fact]
    public void ExtractXamlActions_SetEntityProperty_ReturnsSetValue()
    {
        var xaml = "<mxswa:SetEntityProperty Attribute=\"name\" Value=\"test\" />";
        var actions = WorkflowRag.ExtractXamlActions(xaml);
        Assert.Contains("SET_VALUE", actions);
    }

    [Fact]
    public void ExtractXamlActions_SetAttributeValue_ReturnsSetValue()
    {
        var xaml = "<mcwc:SetAttributeValue Field=\"status\" />";
        var actions = WorkflowRag.ExtractXamlActions(xaml);
        Assert.Contains("SET_VALUE", actions);
    }

    [Fact]
    public void ExtractXamlActions_GetEntityProperty_ReturnsGetValue()
    {
        var xaml = "<mxswa:GetEntityProperty Attribute=\"name\" />";
        var actions = WorkflowRag.ExtractXamlActions(xaml);
        Assert.Contains("GET_VALUE", actions);
    }

    [Fact]
    public void ExtractXamlActions_SetVisibility_ReturnsShowHide()
    {
        var xaml = "<mcwc:SetVisibility Field=\"address1_city\" Value=\"true\" />";
        var actions = WorkflowRag.ExtractXamlActions(xaml);
        Assert.Contains("SHOW_HIDE", actions);
    }

    [Fact]
    public void ExtractXamlActions_UpdateEntity_ReturnsUpdateEntity()
    {
        var xaml = "<mxswa:UpdateEntity EntityName=\"account\" />";
        var actions = WorkflowRag.ExtractXamlActions(xaml);
        Assert.Contains("UPDATE_ENTITY", actions);
    }

    [Fact]
    public void ExtractXamlActions_NoKeywords_ReturnsEmpty()
    {
        var xaml = "<Root><Child /></Root>";
        var actions = WorkflowRag.ExtractXamlActions(xaml);
        Assert.Empty(actions);
    }

    [Fact]
    public void ExtractXamlActions_MultipleKeywords_ReturnsAllTypes()
    {
        var xaml = "<mxswa:SetEntityProperty Attribute=\"x\" /><mxswa:GetEntityProperty Attribute=\"y\" />";
        var actions = WorkflowRag.ExtractXamlActions(xaml);
        Assert.Contains("SET_VALUE", actions);
        Assert.Contains("GET_VALUE", actions);
    }

    // ── ExtractModifiedAttributes ─────────────────────────────────────────────

    [Fact]
    public void ExtractModifiedAttributes_SingleAttribute_ReturnsIt()
    {
        var xaml = @"<mxswa:SetEntityProperty Attribute=""emailaddress1"" Value=""test"" />";
        var attrs = WorkflowRag.ExtractModifiedAttributes(xaml);
        Assert.Single(attrs);
        Assert.Equal("emailaddress1", attrs[0]);
    }

    [Fact]
    public void ExtractModifiedAttributes_MultipleAttributes_ReturnsAllDistinct()
    {
        var xaml =
            @"<mxswa:SetEntityProperty Attribute=""name"" />" +
            @"<mxswa:SetEntityProperty Attribute=""emailaddress1"" />" +
            @"<mxswa:SetEntityProperty Attribute=""name"" />";  // duplicate

        var attrs = WorkflowRag.ExtractModifiedAttributes(xaml);
        Assert.Equal(2, attrs.Count);
        Assert.Contains("name", attrs);
        Assert.Contains("emailaddress1", attrs);
    }

    [Fact]
    public void ExtractModifiedAttributes_GetEntityPropertyIsIgnored()
    {
        // GetEntityProperty should NOT appear in modified list
        var xaml = @"<mxswa:GetEntityProperty Attribute=""name"" />";
        var attrs = WorkflowRag.ExtractModifiedAttributes(xaml);
        Assert.Empty(attrs);
    }

    // ── ExtractReadAttributes ─────────────────────────────────────────────────

    [Fact]
    public void ExtractReadAttributes_GetEntityProperty_ReturnsIt()
    {
        var xaml = @"<mxswa:GetEntityProperty Attribute=""telephone1"" />";
        var attrs = WorkflowRag.ExtractReadAttributes(xaml);
        Assert.Single(attrs);
        Assert.Equal("telephone1", attrs[0]);
    }

    [Fact]
    public void ExtractReadAttributes_SetEntityPropertyIsIgnored()
    {
        var xaml = @"<mxswa:SetEntityProperty Attribute=""name"" />";
        var attrs = WorkflowRag.ExtractReadAttributes(xaml);
        Assert.Empty(attrs);
    }

    // ── GetWorkflowType ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Classic Workflow")]
    [InlineData(2, "Business Rule")]
    public void GetWorkflowType_KnownCategory_ReturnsHumanReadableName(
        int category, string expectedName)
    {
        Assert.Equal(expectedName, WorkflowRag.GetWorkflowType(category));
    }

    [Fact]
    public void GetWorkflowType_UnknownCategory_ReturnsDescriptiveString()
    {
        var result = WorkflowRag.GetWorkflowType(99);
        Assert.Contains("99", result);
        Assert.Contains("Unknown", result, StringComparison.OrdinalIgnoreCase);
    }
}
