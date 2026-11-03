using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

public record ProjectInput(string Name, string? Description, string? Code, int ProjectType, DateTime? StartDateG,
    DateTime? EndDateG, decimal ProjectPrice, decimal BaseBudget, int CurrencyId, int Status,
    int? ParentProjectId, int CategoryId, int CurrentProgressPercentage, int CreatorPersonId,
    int ProjectManagerPersonId);

public record TaskInput(int ProjectId, string Name, DateTime? StartDateG, DateTime? DueDateG,
    int WorkingDays, int ResponcePersonId, string? Description, int ProgressPersentage,
    string? PreviousTasks, decimal StartBudget, int Status, bool Closed);

public record TaskReportInput(int TaskId, DateTime? ReportDateG, string Description, decimal Cost);

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/api/projects").WithTags("projects");

        projects.MapGet("/", async (SadrDbContext db, string? q, int? status, int? categoryId,
            CancellationToken ct = default) =>
        {
            var query = db.Projects.AsNoTracking().Where(p => !p.Deleted);
            if (status is int s) query = query.Where(p => p.Status == s);
            if (categoryId is int c) query = query.Where(p => p.CategoryId == c);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(p => p.Name.Contains(term) || p.Code.Contains(term));
            }

            var rows = await query.OrderByDescending(p => p.StartDateG).ThenBy(p => p.Id)
                .Select(p => new
                {
                    p.Id, p.Name, p.Code, p.Description, p.Status, p.ProjectType, p.StartDate, p.StartDateG,
                    p.EndDate, p.EndDateG, p.ProjectPrice, p.BaseBudget, p.CurrentSpendBudget, p.FinalSpendBudget,
                    p.CurrencyId, p.CategoryId, p.ParentProjectId, p.CurrentProgressPercentage,
                    p.CreatorPersonId, p.ProjectManagerPersonId,
                    ManagerName = db.People.Where(x => x.Id == p.ProjectManagerPersonId)
                        .Select(x => x.FirstName + " " + x.LastName).FirstOrDefault(),
                    CategoryName = db.ProjectCategories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
                    TaskCount = db.ProjectTasks.Count(t => t.ProjectId == p.Id && !t.Deleted),
                    DoneTasks = db.ProjectTasks.Count(t => t.ProjectId == p.Id && !t.Deleted && t.Status == TaskStatusConsts.Done)
                })
                .ToListAsync(ct);

            return Results.Ok(rows);
        });

        projects.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, ct);
            if (project is null) return Results.NotFound();
            var tasks = await db.ProjectTasks.AsNoTracking()
                .Where(t => t.ProjectId == id && !t.Deleted).OrderBy(t => t.Id).ToListAsync(ct);
            var spent = await db.TaskReports.AsNoTracking()
                .Where(r => !r.Deleted && db.ProjectTasks.Any(t => t.Id == r.TaskId && t.ProjectId == id))
                .SumAsync(r => (decimal?)r.Cost, ct) ?? 0m;
            return Results.Ok(new { project, tasks, spent });
        });

        projects.MapPost("/", async (ProjectInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var problem = Validate(body);
            if (problem is not null) return Results.BadRequest(new { message = problem });

            var project = new Project { RecordUniqueId = Guid.NewGuid(), GUID = Guid.NewGuid() };
            Apply(project, body);
            project.StartDate = PersianDate.ToPersian(project.StartDateG);
            project.EndDate = PersianDate.ToPersian(project.EndDateG);
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/projects/{project.Id}", project);
        }).RequireAuthorization(Policies.AnyUser);

        projects.MapPut("/{id:int}", async (int id, ProjectInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, ct);
            if (project is null) return Results.NotFound();
            var problem = Validate(body);
            if (problem is not null) return Results.BadRequest(new { message = problem });

            Apply(project, body);
            project.StartDate = PersianDate.ToPersian(project.StartDateG);
            project.EndDate = PersianDate.ToPersian(project.EndDateG);
            await db.SaveChangesAsync(ct);
            return Results.Ok(project);
        }).RequireAuthorization(Policies.AnyUser);

        projects.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null) return Results.NotFound();
            project.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        // --- tasks -----------------------------------------------------------------------
        app.MapGet("/api/projects/{projectId:int}/tasks", async (int projectId, SadrDbContext db, CancellationToken ct) =>
        {
            var rows = await db.ProjectTasks.AsNoTracking()
                .Where(t => t.ProjectId == projectId && !t.Deleted).OrderBy(t => t.Id)
                .Select(t => new
                {
                    t.Id, t.ProjectId, t.Name, t.StartDate, t.StartDateG, t.DueDate, t.DueDateG,
                    t.WorkingDays, t.ResponcePersonId, t.Description, t.ProgressPersentage,
                    t.PreviousTasks, t.StartBudget, t.Status, t.Closed, t.DoneDescription,
                    t.StartedDate, t.EndedDate,
                    PersonName = db.People.Where(p => p.Id == t.ResponcePersonId)
                        .Select(p => p.FirstName + " " + p.LastName).FirstOrDefault(),
                    SpentCost = db.TaskReports.Where(r => r.TaskId == t.Id && !r.Deleted).Sum(r => (decimal?)r.Cost) ?? 0m
                })
                .ToListAsync(ct);
            return Results.Ok(rows);
        }).WithTags("tasks");

        app.MapPost("/api/tasks", async (TaskInput body, SadrDbContext db, CancellationToken ct) =>
        {
            if (body.Name.Trim().Length == 0) return Results.BadRequest(new { message = "نام وظیفه الزامی است." });
            if (!await db.Projects.AnyAsync(p => p.Id == body.ProjectId && !p.Deleted, ct))
                return Results.BadRequest(new { message = "پروژه یافت نشد." });

            var task = new ProjectTask
            {
                ProjectId = body.ProjectId,
                Name = body.Name.Trim(),
                StartDateG = (body.StartDateG ?? DateTime.Today).Date,
                DueDateG = (body.DueDateG ?? DateTime.Today).Date,
                WorkingDays = body.WorkingDays,
                ResponcePersonId = body.ResponcePersonId,
                Description = body.Description ?? "",
                ProgressPersentage = body.ProgressPersentage,
                PreviousTasks = body.PreviousTasks ?? "",
                StartBudget = body.StartBudget,
                Status = body.Status,
                Closed = body.Closed,
                RecordUniqueId = Guid.NewGuid()
            };
            task.StartDate = PersianDate.ToPersian(task.StartDateG);
            task.DueDate = PersianDate.ToPersian(task.DueDateG);

            db.ProjectTasks.Add(task);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/tasks/{task.Id}", task);
        }).RequireAuthorization(Policies.AnyUser).WithTags("tasks");

        app.MapPost("/api/tasks/{id:int}/progress", async (int id, int progress, HttpContext http,
            SadrDbContext db, CancellationToken ct) =>
        {
            var task = await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == id && !t.Deleted, ct);
            if (task is null) return Results.NotFound();
            if (progress is < 0 or > 100) return Results.BadRequest(new { message = "درصد پیشرفت باید بین ۰ و ۱۰۰ باشد." });

            task.ProgressPersentage = progress;
            if (progress >= 100)
            {
                task.Status = TaskStatusConsts.Done;
                task.EndedDateG = DateTime.Today;
                task.EndedDate = PersianDate.ToPersian(DateTime.Today);
            }
            await db.SaveChangesAsync(ct);

            // Project progress = average of its tasks (what the desktop project page shows).
            var tasks = await db.ProjectTasks.Where(t => t.ProjectId == task.ProjectId && !t.Deleted)
                .Select(t => t.ProgressPersentage).ToListAsync(ct);
            if (tasks.Count > 0)
            {
                var average = (int)Math.Round(tasks.Average());
                await db.Projects.Where(p => p.Id == task.ProjectId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.CurrentProgressPercentage, average), ct);
            }

            return Results.Ok(new { task.Id, task.ProgressPersentage, task.Status });
        }).RequireAuthorization(Policies.AnyUser).WithTags("tasks");

        // --- task reports (cost + description) -------------------------------------------
        app.MapGet("/api/tasks/{taskId:int}/reports", async (int taskId, SadrDbContext db, CancellationToken ct) =>
            Results.Ok(await db.TaskReports.AsNoTracking()
                .Where(r => r.TaskId == taskId && !r.Deleted).OrderByDescending(r => r.Id).ToListAsync(ct)))
            .WithTags("tasks");

        app.MapPost("/api/task-reports", async (TaskReportInput body, SadrDbContext db, CancellationToken ct) =>
        {
            if (!await db.ProjectTasks.AnyAsync(t => t.Id == body.TaskId && !t.Deleted, ct))
                return Results.BadRequest(new { message = "وظیفه یافت نشد." });

            var report = new TaskReport
            {
                TaskId = body.TaskId,
                ReportDate = PersianDate.ToPersian(body.ReportDateG ?? DateTime.Today),
                Description = body.Description ?? "",
                Cost = body.Cost,
                RecordUniqueId = Guid.NewGuid()
            };
            db.TaskReports.Add(report);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/task-reports/{report.Id}", report);
        }).RequireAuthorization(Policies.AnyUser).WithTags("tasks");
    }

    private static string? Validate(ProjectInput body)
    {
        if (string.IsNullOrWhiteSpace(body.Name)) return "نام پروژه الزامی است.";
        if (body.CategoryId <= 0) return "دسته‌بندی پروژه الزامی است.";
        if (body.CurrencyId <= 0) return "واحد پول الزامی است.";
        if (body.StartDateG is not null && body.EndDateG is not null && body.EndDateG < body.StartDateG)
            return "تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد.";
        return null;
    }

    private static void Apply(Project project, ProjectInput body)
    {
        project.Name = body.Name.Trim();
        project.Description = body.Description ?? "";
        project.Code = body.Code ?? "";
        project.ProjectType = body.ProjectType;
        project.StartDateG = (body.StartDateG ?? DateTime.Today).Date;
        project.EndDateG = (body.EndDateG ?? body.StartDateG ?? DateTime.Today).Date;
        project.ProjectPrice = body.ProjectPrice;
        project.BaseBudget = body.BaseBudget;
        project.CurrencyId = body.CurrencyId;
        project.Status = body.Status;
        project.ParentProjectId = body.ParentProjectId is > 0 ? body.ParentProjectId : null;
        project.CategoryId = body.CategoryId;
        project.CurrentProgressPercentage = body.CurrentProgressPercentage;
        project.CreatorPersonId = body.CreatorPersonId;
        project.ProjectManagerPersonId = body.ProjectManagerPersonId;
    }
}
