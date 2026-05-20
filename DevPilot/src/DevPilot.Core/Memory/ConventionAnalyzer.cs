using System;
using System.Collections.Generic;

namespace DevPilot.Core.Memory;

public sealed record RepositoryConventions(
    bool PrefixInterfacesWithI,
    bool SuffixAsyncMethods,
    string PrivateFieldPrefix,
    string LoggingLibrary,
    string DiStyle);

public sealed class ConventionAnalyzer
{
    public RepositoryConventions AnalyzeConventions(IReadOnlyList<string> sampleFileContents)
    {
        bool prefixInterfaces = true;
        bool suffixAsync = true;
        string privatePrefix = "_";
        string logging = "Microsoft.Extensions.Logging";
        string diStyle = "Microsoft.Extensions.DependencyInjection";

        if (sampleFileContents.Count > 0)
        {
            int asyncMethodCount = 0;
            int asyncMethodSuffixCount = 0;
            int interfaceCount = 0;
            int interfaceWithICount = 0;
            int underscoreFields = 0;
            int normalFields = 0;

            foreach (var content in sampleFileContents)
            {
                if (content.Contains("async Task"))
                {
                    asyncMethodCount++;
                    if (content.Contains("Async(")) asyncMethodSuffixCount++;
                }
                if (content.Contains("interface "))
                {
                    interfaceCount++;
                    if (content.Contains("interface I")) interfaceWithICount++;
                }
                if (content.Contains("private ") && content.Contains(";"))
                {
                    if (content.Contains(" _")) underscoreFields++;
                    else normalFields++;
                }
                if (content.Contains("console.log") || content.Contains("console.error"))
                {
                    logging = "console";
                }
            }

            if (interfaceCount > 0) prefixInterfaces = (double)interfaceWithICount / interfaceCount >= 0.75;
            if (asyncMethodCount > 0) suffixAsync = (double)asyncMethodSuffixCount / asyncMethodCount >= 0.75;
            if (underscoreFields + normalFields > 0) privatePrefix = underscoreFields >= normalFields ? "_" : "";
        }

        return new RepositoryConventions(prefixInterfaces, suffixAsync, privatePrefix, logging, diStyle);
    }
}
