using System.Text;

namespace BioTwin_AI.DotNetShared.Resumes;

public static class ResumeWizardMarkdownBuilder
{
    public static string Build(ResumeWizardDto resume)
    {
        ArgumentNullException.ThrowIfNull(resume);

        var isEnglish = string.Equals(resume.Language, ResumeLanguages.English, StringComparison.OrdinalIgnoreCase);
        var title = FirstNonBlank(resume.Title, resume.Profile.FullName, isEnglish ? "Resume" : "个人简历");
        var builder = new StringBuilder()
            .Append("# ").AppendLine(title)
            .AppendLine();

        AppendProfile(builder, resume.Profile);
        AppendTextSection(builder, isEnglish ? "Summary" : "职业摘要", resume.Summary);
        AppendExperiences(builder, resume.Experiences ?? [], isEnglish);
        AppendEducation(builder, resume.Education ?? [], isEnglish);
        AppendSkills(builder, resume.Skills ?? [], isEnglish);
        AppendProjects(builder, resume.Projects ?? [], isEnglish);
        AppendListSection(builder, isEnglish ? "Additional Information" : "其他信息", resume.AdditionalSections ?? []);

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendProfile(StringBuilder builder, ResumeWizardProfileDto profile)
    {
        var contactItems = new[] { profile.Email, profile.Location, profile.Website }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        if (contactItems.Length == 0)
        {
            return;
        }

        builder.AppendLine(string.Join(" | ", contactItems)).AppendLine();
    }

    private static void AppendTextSection(StringBuilder builder, string heading, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append("## ").AppendLine(heading)
            .AppendLine()
            .AppendLine(value.Trim())
            .AppendLine();
    }

    private static void AppendExperiences(
        StringBuilder builder,
        IReadOnlyList<ResumeWizardExperienceDto> experiences,
        bool isEnglish)
    {
        var items = experiences.Where(item => !string.IsNullOrWhiteSpace(item.Company) || !string.IsNullOrWhiteSpace(item.Role)).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.Append("## ").AppendLine(isEnglish ? "Experience" : "工作经历").AppendLine();
        foreach (var item in items)
        {
            var heading = JoinHeading(item.Role, item.Company);
            builder.Append("### ").AppendLine(heading);
            AppendPeriod(builder, item.StartDate, item.EndDate, isEnglish);
            foreach (var highlight in item.Highlights.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                builder.Append("- ").AppendLine(highlight.Trim());
            }

            builder.AppendLine();
        }
    }

    private static void AppendEducation(
        StringBuilder builder,
        IReadOnlyList<ResumeWizardEducationDto> education,
        bool isEnglish)
    {
        var items = education.Where(item => !string.IsNullOrWhiteSpace(item.Institution) || !string.IsNullOrWhiteSpace(item.Qualification)).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.Append("## ").AppendLine(isEnglish ? "Education" : "教育经历").AppendLine();
        foreach (var item in items)
        {
            builder.Append("### ").AppendLine(JoinHeading(item.Qualification, item.Institution));
            AppendPeriod(builder, item.StartDate, item.EndDate, isEnglish);
            builder.AppendLine();
        }
    }

    private static void AppendSkills(StringBuilder builder, IReadOnlyList<string> skills, bool isEnglish)
    {
        var items = skills.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.Append("## ").AppendLine(isEnglish ? "Skills" : "专业技能")
            .AppendLine()
            .AppendLine(string.Join(" · ", items))
            .AppendLine();
    }

    private static void AppendProjects(
        StringBuilder builder,
        IReadOnlyList<ResumeWizardProjectDto> projects,
        bool isEnglish)
    {
        var items = projects.Where(item => !string.IsNullOrWhiteSpace(item.Name)).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.Append("## ").AppendLine(isEnglish ? "Projects" : "项目经历").AppendLine();
        foreach (var item in items)
        {
            builder.Append("### ").AppendLine(item.Name.Trim());
            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                builder.AppendLine(item.Description.Trim());
            }

            var technologies = item.Technologies.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();
            if (technologies.Length > 0)
            {
                builder.Append(isEnglish ? "Technologies: " : "技术栈：").AppendLine(string.Join(", ", technologies));
            }

            builder.AppendLine();
        }
    }

    private static void AppendListSection(StringBuilder builder, string heading, IReadOnlyList<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.Append("## ").AppendLine(heading).AppendLine();
        foreach (var item in items)
        {
            builder.Append("- ").AppendLine(item);
        }

        builder.AppendLine();
    }

    private static void AppendPeriod(StringBuilder builder, string? startDate, string? endDate, bool isEnglish)
    {
        if (string.IsNullOrWhiteSpace(startDate) && string.IsNullOrWhiteSpace(endDate))
        {
            return;
        }

        builder.Append(FirstNonBlank(startDate, isEnglish ? "Unknown" : "未注明"))
            .Append(" - ")
            .AppendLine(FirstNonBlank(endDate, isEnglish ? "Present" : "至今"));
    }

    private static string JoinHeading(string? first, string? second)
    {
        return string.Join(" - ", new[] { first, second }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }
}
