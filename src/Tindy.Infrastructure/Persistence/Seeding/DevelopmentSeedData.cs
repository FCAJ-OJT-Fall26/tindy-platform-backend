namespace Tindy.Infrastructure.Persistence.Seeding;

internal static class DevelopmentSeedData
{
    internal sealed record Persona(string Name, string Role, string[] Skills);
    internal sealed record ProjectTrack(string Category, string Role, string[] Required, string[] Optional, string[] Titles);

    internal static readonly Persona[] Personas =
    [
        new("Nguyễn Minh An", "Backend Developer", ["C#", ".NET", "ASP.NET Core", "Entity Framework Core", "PostgreSQL", "xUnit"]),
        new("Trần Thu Hà", "Frontend Developer", ["React", "TypeScript", "Tailwind CSS", "Vite", "Vitest", "Accessibility"]),
        new("Lê Hoàng Nam", "Cloud Engineer", ["AWS", "Docker", "Terraform", "Linux", "GitHub Actions", "Prometheus"]),
        new("Phạm Ngọc Linh", "Mobile Developer", ["Flutter", "Dart", "REST API", "SQLite", "Git"]),
        new("Võ Gia Huy", "AI Engineer", ["Python", "Machine Learning", "scikit-learn", "pandas", "NumPy", "FastAPI"]),
        new("Đặng Bảo Trâm", "UI/UX Designer", ["Figma", "Wireframing", "Prototyping", "User Research", "Accessibility"]),
        new("Bùi Đức Anh", "Backend Developer", ["Java", "Spring Boot", "MySQL", "Redis", "RabbitMQ", "Docker"]),
        new("Ngô Khánh Vy", "Frontend Developer", ["Vue.js", "TypeScript", "HTML", "CSS", "Vite", "Cypress"]),
        new("Dương Quốc Bảo", "Full-stack Developer", ["Node.js", "NestJS", "React", "TypeScript", "PostgreSQL", "JWT"]),
        new("Hồ Phương Nhi", "QA Engineer", ["Playwright", "Postman", "SQL", "Test Automation", "Test Planning", "Agile"]),
        new("Đỗ Tuấn Kiệt", "DevOps Engineer", ["AWS", "Kubernetes", "Docker", "CI/CD", "Grafana", "Linux"]),
        new("Nguyễn Mai Anh", "Data Engineer", ["SQL", "Python", "pandas", "Apache Airflow", "dbt", "PostgreSQL"]),
        new("Trần Hải Đăng", "Mobile Developer", ["React Native", "TypeScript", "REST API", "Jest", "Git"]),
        new("Lê Thanh Thảo", "Business Analyst", ["Requirements Analysis", "User Research", "Agile", "Scrum", "SQL"]),
        new("Phan Nhật Minh", "Backend Developer", ["Python", "Django", "FastAPI", "PostgreSQL", "Docker", "OpenAPI"]),
        new("Vũ Ngọc Ánh", "Frontend Developer", ["Angular", "TypeScript", "CSS", "Jest", "Accessibility"]),
        new("Hoàng Việt Long", "Full-stack Developer", ["Next.js", "React", "Node.js", "TypeScript", "OAuth 2.0", "MongoDB"]),
        new("Đinh Yến Nhi", "AI Engineer", ["Python", "PyTorch", "TensorFlow", "Machine Learning", "NumPy", "Docker"]),
        new("Trương Quang Vinh", "QA Engineer", ["C#", "xUnit", "Postman", "OpenAPI", "SQL", "Test Planning"]),
        new("Lý Minh Châu", "UI/UX Designer", ["Figma", "User Research", "Prototyping", "HTML", "CSS", "Scrum"]),
    ];

    internal static readonly ProjectTrack[] Tracks =
    [
        new("Web Application", "Backend Developer", ["C#", "ASP.NET Core"], ["Entity Framework Core", "PostgreSQL"],
            ["Campus Room Booking", "Student Marketplace", "Club Membership Hub", "Library Reservation", "Internship Progress Tracker"]),
        new("Web Application", "Full-stack Developer", ["Node.js", "TypeScript"], ["NestJS", "React"],
            ["Study Group Finder", "Community Event Board", "Personal Budget Planner", "Peer Review Workspace", "Volunteer Registration"]),
        new("Open Source", "Frontend Developer", ["React", "TypeScript"], ["Accessibility", "Vitest"],
            ["Accessible Component Kit", "Developer Portfolio Builder", "Learning Roadmap Board", "Markdown Notes Workspace", "Open Source Issue Explorer"]),
        new("Cloud & DevOps", "Cloud Engineer", ["AWS", "Docker"], ["Terraform", "GitHub Actions"],
            ["Cloud Cost Dashboard", "Serverless Event Platform", "Infrastructure Sandbox", "Container Deployment Lab", "Cloud Monitoring Portal"]),
        new("Mobile App", "Mobile Developer", ["Flutter", "Dart"], ["REST API", "SQLite"],
            ["Campus Shuttle Companion", "Study Habit Tracker", "Local Food Sharing", "Fitness Challenge Mobile", "Travel Checklist Companion"]),
        new("Mobile App", "Mobile Developer", ["React Native", "TypeScript"], ["Jest", "REST API"],
            ["Student Expense Wallet", "Language Exchange Mobile", "Volunteer Check-in", "Book Exchange Mobile", "Neighborhood Recycling"]),
        new("AI & Machine Learning", "AI Engineer", ["Python", "Machine Learning"], ["scikit-learn", "FastAPI"],
            ["Plant Health Research", "Learning Resource Classifier", "Public Transport Forecast", "Recycling Image Dataset", "Energy Usage Forecast"]),
        new("Open Source", "Data Engineer", ["SQL", "Python"], ["Apache Airflow", "dbt"],
            ["City Open Data Pipeline", "Air Quality Analytics", "Public Library Data Hub", "Student Survey Analytics", "Community Metrics Pipeline"]),
        new("Social Impact", "UI/UX Designer", ["Figma", "User Research"], ["Prototyping", "Accessibility"],
            ["Inclusive Campus Map", "Accessible Volunteer Portal", "Community Health Directory", "Food Donation Experience", "Senior Digital Helpdesk"]),
        new("Social Impact", "Business Analyst", ["Requirements Analysis", "Agile"], ["Scrum", "SQL"],
            ["Scholarship Application Portal", "Community Donation Tracker", "Student Mentoring Network", "Sustainable Campus Planner", "Local Skills Exchange"]),
    ];

    // Dedicated stable namespaces; existing IDs/roots are never replaced on a rerun.
    internal static Guid Id(int kind, int ordinal) => Guid.Parse($"d3e00000-000{kind}-4000-8000-{ordinal:000000000000}");
    internal static string Username(int ordinal) => $"demo{ordinal:00}";
    internal static string Email(int ordinal) => $"demo{ordinal:00}@tindy.test";
}
