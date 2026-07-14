using System.Text.Json;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class SchemaContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, object> Schemas => new()
    {
        { "source analysis", ScriptService.CreateSourceAnalysisSchema() },
        { "concept selection", ScriptService.CreateConceptSelectionSchema() },
        { "script", ScriptService.CreateScriptSchema() },
        { "content review", ScriptService.CreateContentReviewSchema() },
        { "visual plan", ScriptService.CreateVisualPlanSchema() }
    };

    [Fact]
    public void ScriptSchema_SceneRequiresOnScreenText()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ScriptService.CreateScriptSchema(), JsonOptions));

        var sceneItems = document.RootElement
            .GetProperty("properties")
            .GetProperty("scenes")
            .GetProperty("items");

        var required = sceneItems
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("onScreenText", required);
    }

    [Theory]
    [MemberData(nameof(Schemas))]
    public void Schemas_ObjectPropertiesExactlyMatchRequiredFields(string schemaName, object schema)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(schema, JsonOptions));

        AssertSchemaObject(document.RootElement, schemaName);
    }

    private static void AssertSchemaObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("properties", out var properties))
        {
            Assert.Equal(JsonValueKind.Object, properties.ValueKind);
            Assert.True(
                element.TryGetProperty("additionalProperties", out var additionalProperties),
                $"{path}: object schema must define additionalProperties.");
            Assert.Equal(JsonValueKind.False, additionalProperties.ValueKind);
            Assert.True(
                element.TryGetProperty("required", out var required),
                $"{path}: object schema must define required.");
            Assert.Equal(JsonValueKind.Array, required.ValueKind);

            var propertyNames = properties.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var requiredNames = required.EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(propertyNames, requiredNames);

            foreach (var property in properties.EnumerateObject())
            {
                AssertSchemaObject(property.Value, $"{path}.properties.{property.Name}");
            }
        }

        if (element.TryGetProperty("items", out var items))
        {
            AssertSchemaObjectOrArray(items, $"{path}.items");
        }

        if (element.TryGetProperty("$defs", out var defs))
        {
            foreach (var def in defs.EnumerateObject())
            {
                AssertSchemaObject(def.Value, $"{path}.$defs.{def.Name}");
            }
        }

        AssertSchemaArray(element, "anyOf", path);
        AssertSchemaArray(element, "oneOf", path);
        AssertSchemaArray(element, "allOf", path);
    }

    private static void AssertSchemaArray(JsonElement element, string propertyName, string path)
    {
        if (!element.TryGetProperty(propertyName, out var schemas))
        {
            return;
        }

        Assert.Equal(JsonValueKind.Array, schemas.ValueKind);
        var index = 0;
        foreach (var schema in schemas.EnumerateArray())
        {
            AssertSchemaObject(schema, $"{path}.{propertyName}[{index}]");
            index++;
        }
    }

    private static void AssertSchemaObjectOrArray(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                AssertSchemaObject(item, $"{path}[{index}]");
                index++;
            }

            return;
        }

        AssertSchemaObject(element, path);
    }
}
