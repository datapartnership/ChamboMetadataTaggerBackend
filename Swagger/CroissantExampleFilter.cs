using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MetadataTagging.Swagger;

/// <summary>
/// Injects a realistic Croissant JSON-LD example into the Swagger UI
/// for the query endpoint's 200 response, so the "Example Value" exactly
/// matches the real API output.
/// </summary>
public class CroissantExampleFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Only target the Query action
        if (context.MethodInfo.Name != "Query")
            return;

        if (!operation.Responses.TryGetValue("200", out var response))
            return;

        var example = new OpenApiObject
        {
            ["@context"] = new OpenApiArray
            {
                new OpenApiString("https://mlcommons.org/working-groups/data/croissant/"),
                new OpenApiObject
                {
                    ["sc"]   = new OpenApiString("https://schema.org/"),
                    ["dct"]  = new OpenApiString("http://purl.org/dc/terms/"),
                    ["dqv"]  = new OpenApiString("http://www.w3.org/ns/dqv#"),
                    ["cr"]   = new OpenApiString("http://mlcommons.org/croissant/"),
                    ["ddpv"] = new OpenApiString("https://datapartnership.org/ddpv-metadata-terms"),
                    ["olac"] = new OpenApiString("http://www.language-archives.org/OLAC/1.1/"),
                    ["dcat"] = new OpenApiString("http://www.w3.org/ns/dcat#"),
                    ["prov"] = new OpenApiString("http://www.w3.org/ns/prov#"),
                    ["rai"]  = new OpenApiString("http://mlcommons.org/croissant/RAI/"),
                    ["rdfs"] = new OpenApiString("http://www.w3.org/2000/01/rdf-schema#")
                }
            },
            ["generatedAt"] = new OpenApiString("2026-03-02T12:00:00.0000000Z"),
            ["samplingInfo"] = new OpenApiObject
            {
                ["totalMatched"] = new OpenApiInteger(142),
                ["limit"]        = new OpenApiInteger(30),
                ["returned"]     = new OpenApiInteger(30),
                ["randomSample"] = new OpenApiBoolean(true)
            },
            ["@graph"] = new OpenApiArray
            {
                new OpenApiObject
                {
                    ["@type"]                = new OpenApiString("sc:Dataset"),
                    ["@id"]                  = new OpenApiString("nation-2024"),
                    ["sc:name"]              = new OpenApiString("Nation Newspaper 2024"),
                    ["sc:temporalCoverage"]  = new OpenApiString("2024"),
                    ["sc:spatialCoverage"]   = new OpenApiString("Malawi"),
                    ["sc:inLanguage"]        = new OpenApiString("ny"),
                    ["sc:creator"] = new OpenApiObject
                    {
                        ["@type"]   = new OpenApiString("sc:Organization"),
                        ["sc:name"] = new OpenApiString("Nation Publications Limited")
                    },
                    ["sc:publisher"] = new OpenApiObject
                    {
                        ["@type"]   = new OpenApiString("sc:Organization"),
                        ["sc:name"] = new OpenApiString("Nation Publications Limited")
                    },
                    ["sc:license"]        = new OpenApiString("LicenseRef-InternalDemoOnly"),
                    ["sc:version"]        = new OpenApiString("v1.0.0"),
                    ["cr:isLiveDataset"]  = new OpenApiBoolean(false),
                    ["distribution"] = new OpenApiArray
                    {
                        new OpenApiObject
                        {
                            ["@type"]              = new OpenApiString("cr:FileObject"),
                            ["@id"]                = new OpenApiString("nation-2024/nation-2024-01-15.pdf"),
                            ["sc:name"]            = new OpenApiString("nation-2024-01-15.pdf"),
                            ["sc:contentUrl"]      = new OpenApiString("https://localhost:5001/api/files/file-2024-001005c4"),
                            ["sc:encodingFormat"]  = new OpenApiString("application/pdf"),
                            ["sc:contentSize"]     = new OpenApiLong(204800),
                            ["cr:sha256"]          = new OpenApiString("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
                            ["sc:author"]          = new OpenApiString("John Banda"),
                            ["sc:datePublished"]   = new OpenApiString("2024-01-15"),
                            ["sc:dateModified"]    = new OpenApiString("2024-01-16"),
                            ["sc:inLanguage"]      = new OpenApiString("ny"),
                            ["sc:numberOfPages"]   = new OpenApiInteger(4),
                            ["sc:wordCount"]       = new OpenApiInteger(1500),
                            ["ddpv:tokenCount"]    = new OpenApiInteger(120),
                            ["ddpv:charCount"]     = new OpenApiInteger(8500),
                            ["dct:subject"]        = new OpenApiString("news"),
                            ["olac:discourseType"] = new OpenApiString("news article"),
                            ["sc:keywords"] = new OpenApiArray
                            {
                                new OpenApiString("malaria"),
                                new OpenApiString("health")
                            },
                            ["dcat:theme"] = new OpenApiArray
                            {
                                new OpenApiString("HEALTH"),
                                new OpenApiString("DISEASE CONTROL")
                            },
                            ["dcat:themeTaxonomy"] = new OpenApiString("https://vocabularyserver.com/unbis/en/index.php")
                        }
                    }
                }
            }
        };

        // Set the example on every media type in the 200 response
        foreach (var mediaType in response.Content.Values)
        {
            mediaType.Example = example;
        }
    }
}
