using System.Text.RegularExpressions;

namespace Tindy.Infrastructure.Persistence.Seeding;

// Catalog IDs are stable across environments. Never change an existing ID after deployment.
internal static class CatalogSeedData
{
    internal sealed record NamedItem(Guid Id, string Name, string? Description = null, int DisplayOrder = 0);

    internal static readonly IReadOnlyList<NamedItem> ProfessionalRoles =
    [
        new(Guid.Parse("e8e72cb0-e5b2-515a-92b3-1abf59733d97"), "Backend Developer", "Build APIs, services and backend systems.", 1),
        new(Guid.Parse("eb27be56-fcbf-56aa-8423-c4680c6dfee6"), "Frontend Developer", "Build interactive web interfaces.", 2),
        new(Guid.Parse("2b880d23-6c99-55b5-a13c-83d4f2e40ac6"), "Full-stack Developer", "Develop both frontend and backend applications.", 3),
        new(Guid.Parse("2aa1b823-b582-50c6-8f18-7071cee37573"), "AI Engineer", "Build and integrate AI-powered solutions.", 4),
        new(Guid.Parse("ae78e4b0-bdf9-5e7a-a285-e243bac29dba"), "Cloud Engineer", "Design and operate cloud infrastructure.", 5),
        new(Guid.Parse("280374ac-26b5-5ed3-a647-3587ba1486fb"), "UI/UX Designer", "Design user interfaces and experiences.", 6),
        new(Guid.Parse("55384d5a-dec6-55c9-a555-56e9f6afb857"), "Mobile Developer", "Build native or cross-platform mobile applications.", 7),
        new(Guid.Parse("77f177ce-538e-5be1-8568-d75549782c76"), "DevOps Engineer", "Build delivery pipelines and operate deployment systems.", 8),
        new(Guid.Parse("4d11cbf9-a1ae-535d-9d9c-d6d832dad0cd"), "Data Engineer", "Build data pipelines and data platforms.", 9),
        new(Guid.Parse("6cac1172-ad63-5c90-bafb-eb37ab575a8f"), "QA Engineer", "Test software and improve product quality.", 10),
        new(Guid.Parse("02ee895e-c50c-50ed-aa2b-c7826b28c30a"), "Business Analyst", "Analyze requirements and business processes.", 11),
    ];

    internal static readonly IReadOnlyList<NamedItem> SkillCategories =
    [
        new(Guid.Parse("04150825-0400-5880-bfe2-c5a725c341fb"), "Backend Development", "Server-side services, APIs and application logic.", 1),
        new(Guid.Parse("d308cc8f-aedb-573b-91f6-d0763ab14bc1"), "Frontend Development", "Web interfaces and client-side applications.", 2),
        new(Guid.Parse("8c6327a1-0ed1-5037-9424-c7307fde5712"), "Mobile Development", "Mobile applications and cross-platform tools.", 3),
        new(Guid.Parse("d942371a-c833-5eb2-9a9b-d18bdfbdfb75"), "Cloud & DevOps", "Cloud, infrastructure and software delivery.", 4),
        new(Guid.Parse("89f9b2a4-4d5f-5901-95c0-01fe22116a05"), "AI & Machine Learning", "Artificial intelligence and machine learning.", 5),
        new(Guid.Parse("10924a1c-96ce-579a-a878-6cbf41e53f03"), "Database", "Relational and non-relational data technologies.", 6),
        new(Guid.Parse("e406ceef-f3fe-5c39-a1e5-0f2ae4cd969a"), "UI/UX Design", "Product interface and experience design.", 7),
    ];

    internal static readonly IReadOnlyList<NamedItem> Skills =
    [
        new(Guid.Parse("e0027d9c-48c9-5496-8904-658b13776f4c"), "C#"),
        new(Guid.Parse("119c99fb-fe58-53dc-9414-8bdbf8fbbeb2"), ".NET"),
        new(Guid.Parse("79cd938f-1823-5ac8-85c7-dac4d1dff8f1"), "ASP.NET Core"),
        new(Guid.Parse("1da4e534-0509-5f8f-8770-a09c1e127d85"), "Java"),
        new(Guid.Parse("04051696-d901-504d-8cd4-cba9991f5404"), "Spring Boot"),
        new(Guid.Parse("0240136a-657d-5557-8d95-78cb57666d34"), "Node.js"),
        new(Guid.Parse("837fc321-7450-5259-8d83-015e6a0824f2"), "Python"),
        new(Guid.Parse("c425c55c-2a2c-5833-8027-2374685249b5"), "Go"),
        new(Guid.Parse("f844352d-8ac2-517b-9d80-1c0a72600185"), "PHP"),
        new(Guid.Parse("fb68e239-8d27-50e0-bd4c-4fdc3f20be1c"), "Laravel"),
        new(Guid.Parse("3e889694-2b72-5d5e-82e8-ab225071bd07"), "REST API"),
        new(Guid.Parse("07b613ef-04a6-565d-89b3-d93099d5518b"), "GraphQL"),
        new(Guid.Parse("5da7104e-19f3-52ba-9e21-6e1a3d94a3e6"), "Docker"),
        new(Guid.Parse("14526cc7-e056-512e-a714-75bf2ad53b8b"), "TypeScript"),
        new(Guid.Parse("a5f0dfba-6a2b-56fe-80d4-a51dacac0f4a"), "HTML"),
        new(Guid.Parse("2d5f8d69-655d-5afd-a424-aec78cb5ac5c"), "CSS"),
        new(Guid.Parse("bd7dedad-8e96-5e84-9f68-fc866b85d018"), "JavaScript"),
        new(Guid.Parse("e2763dc8-afc1-5705-be3e-12a48bf1dd09"), "React"),
        new(Guid.Parse("f0c3aa29-254a-5a23-91ac-2211180221b0"), "Angular"),
        new(Guid.Parse("ac6c8d98-e9fd-589d-8902-5f840963d868"), "Vue.js"),
        new(Guid.Parse("a6ce5178-d1db-50ad-9d36-218b70d57d3a"), "Next.js"),
        new(Guid.Parse("1aa8b3a0-50d6-55bd-b587-dad9fd158e16"), "Tailwind CSS"),
        new(Guid.Parse("a38e7c43-9baf-5bf1-8110-fdf75d717047"), "Git"),
        new(Guid.Parse("13019aeb-8a26-51e3-8e4b-3eefe276a6d2"), "Flutter"),
        new(Guid.Parse("26c74537-6bc3-5678-8140-de100d315ffe"), "Dart"),
        new(Guid.Parse("3446c8a5-79e3-5bf9-945d-d85149434b55"), "Kotlin"),
        new(Guid.Parse("265f6942-3e6e-5f31-9797-cd1e1948ebb3"), "Swift"),
        new(Guid.Parse("a9bca54c-945c-5f3b-8c79-a37b7906cddb"), "React Native"),
        new(Guid.Parse("f90232b7-a11c-5d32-be00-908af85c7267"), "AWS"),
        new(Guid.Parse("803ee6d2-2f5c-5393-8a30-228d7d1b0888"), "Azure"),
        new(Guid.Parse("5e02d195-058f-582e-bb08-d4bd56568c85"), "Kubernetes"),
        new(Guid.Parse("f99924f7-7a38-5671-b39e-364735244a71"), "Terraform"),
        new(Guid.Parse("81d60d66-e5ad-5a7b-b378-48b444675fdd"), "Linux"),
        new(Guid.Parse("62756920-15ca-5a23-84f0-7fe707396142"), "GitHub Actions"),
        new(Guid.Parse("be27554f-76c5-5fe9-908e-1460e5ee9a57"), "CI/CD"),
        new(Guid.Parse("7eef27d3-5b3e-594a-a628-91ce887c933c"), "PyTorch"),
        new(Guid.Parse("a5cfb56d-06fb-5e6f-9519-bbc82aadf1fc"), "TensorFlow"),
        new(Guid.Parse("1b57ba0f-6f5d-5724-9027-e9c18dd80036"), "RAG"),
        new(Guid.Parse("ea60ad62-936b-5044-a633-57c8990dbe32"), "LangChain"),
        new(Guid.Parse("280ab2e6-3bbe-5a24-9872-f1c799a1954b"), "Machine Learning"),
        new(Guid.Parse("07c390fa-bed8-5666-a7a3-4d285146ab5a"), "PostgreSQL"),
        new(Guid.Parse("e9a7bbd0-1314-5400-b1b8-1f0594cfe908"), "MySQL"),
        new(Guid.Parse("16f84349-1aeb-5d6c-9a3a-f855d484c513"), "MongoDB"),
        new(Guid.Parse("e6815f7d-12c4-5935-901a-b7062eedd082"), "Redis"),
        new(Guid.Parse("5ad523af-cae3-58c2-93b8-922b1109c868"), "SQL Server"),
        new(Guid.Parse("e126139e-c836-5d10-a899-e1217adbc7eb"), "DynamoDB"),
        new(Guid.Parse("b52138f5-6f0f-57b9-8801-e1394c243ab5"), "Figma"),
        new(Guid.Parse("0a34c6d2-83d6-565e-b007-661ccd2b3ecc"), "Wireframing"),
        new(Guid.Parse("36edd839-58e6-5de5-a2c5-b8cee42ce7fc"), "Prototyping"),
    ];

    internal static readonly IReadOnlyDictionary<string, string[]> CategorySkills =
        new Dictionary<string, string[]>
        {
            ["Backend Development"] = ["C#", ".NET", "ASP.NET Core", "Java", "Spring Boot", "Node.js", "Python", "Go", "PHP", "Laravel", "REST API", "GraphQL", "Docker", "TypeScript"],
            ["Frontend Development"] = ["HTML", "CSS", "JavaScript", "TypeScript", "React", "Angular", "Vue.js", "Next.js", "Tailwind CSS", "Git"],
            ["Mobile Development"] = ["Flutter", "Dart", "Kotlin", "Swift", "React Native", "Git"],
            ["Cloud & DevOps"] = ["AWS", "Azure", "Docker", "Kubernetes", "Terraform", "Linux", "Git", "GitHub Actions", "CI/CD"],
            ["AI & Machine Learning"] = ["Python", "PyTorch", "TensorFlow", "RAG", "LangChain", "Machine Learning"],
            ["Database"] = ["PostgreSQL", "MySQL", "MongoDB", "Redis", "SQL Server", "DynamoDB"],
            ["UI/UX Design"] = ["Figma", "Wireframing", "Prototyping"],
        };

    // Match the existing onboarding normalization (collapse whitespace + lowercase invariant).
    internal static string Normalize(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();
}
