using Npgsql;
using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Database;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

// docs/architecture/projects.md: projects of several recordings, in the
// catalog, and the tasks of a project's recordings taken together.
public sealed class ProjectCatalogTests(EmbeddedPostgresFixture fixture) : IClassFixture<EmbeddedPostgresFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string> NoProfiles = [];

    private RecordingStore Store => new(fixture.Server.DataSource);

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task AProject_IsCreatedOnce_ByItsTrimmedName_AndRenamedOnlyToAFreeName()
    {
        var name = Unique("Checkout");
        var created = await Store.CreateProjectAsync("  " + name + " ", Token);
        Assert.NotNull(created);
        Assert.Null(await Store.CreateProjectAsync(name, Token));

        var other = (await Store.CreateProjectAsync(Unique("Sign in"), Token))!.Value;
        Assert.False(await Store.RenameProjectAsync(other, name, Token));
        var renamed = Unique("Basket");
        Assert.True(await Store.RenameProjectAsync(other, renamed, Token));
        Assert.False(await Store.RenameProjectAsync(Guid.NewGuid(), Unique("Gone"), Token));

        var projects = await Store.ProjectsAsync(Token);
        Assert.Contains(projects, project => project.ProjectId == created && project.Name == name && project.Recordings == 0);
        Assert.Contains(projects, project => project.ProjectId == other && project.Name == renamed);
    }

    [Fact]
    public async Task Recordings_AreInTheProjectTheyAreMadeIn_AndMoveWithTheirOwnTasks()
    {
        var first = (await Store.CreateProjectAsync(Unique("First"), Token))!.Value;
        var second = (await Store.CreateProjectAsync(Unique("Second"), Token))!.Value;
        var early = await RecordingAsync(first, DateTimeOffset.UtcNow.AddHours(-2));
        var late = await RecordingAsync(first, DateTimeOffset.UtcNow.AddHours(-1));
        var catalog = AnalysisCatalog.For(fixture.Server.DataSource);
        var own = await catalog.QueueTaskAsync(late.Id, AndroidReportKind.Name, 1, NoProfiles, cancellationToken: Token);
        var whole = await catalog.QueueProjectTaskAsync(second, AndroidProjectReportKind.Name, 1, NoProfiles, @"C:\Recordings", cancellationToken: Token);

        // In the order they were made.
        Assert.Equal([early.Key, late.Key], (await Store.RecordingsOfAsync(first, Token)).Select(recording => recording.SessionKey));
        Assert.Equal(first, await Store.ProjectOfAsync(late.Key, Token));

        // Moving one already there does nothing; the other moves with its task.
        Assert.Equal(1, await Store.MoveRecordingsAsync([late.Id], second, Token));
        Assert.Equal(0, await Store.MoveRecordingsAsync([late.Id], second, Token));
        Assert.Equal(second, await Store.ProjectOfAsync(late.Key, Token));
        Assert.Equal((second, (await Store.ProjectsAsync(Token)).Single(project => project.ProjectId == second).Name),
            await catalog.ProjectOfAsync(late.Id, Token));

        // The recording's tasks are its own and its project's.
        var tasks = await catalog.TasksAsync(late.Id, Token);
        Assert.Equal([whole, own], tasks.Select(task => task.TaskId));
        var project = tasks.Single(task => task.TaskId == whole);
        Assert.Equal((null, second, @"C:\Recordings"), (project.RecordingId, project.ProjectId, project.SessionDirectory));
        Assert.DoesNotContain(await catalog.TasksAsync(early.Id, Token), task => task.TaskId == whole || task.TaskId == own);
    }

    [Fact]
    public async Task AProjectsTask_IsOfTheProject_NotOfARecording_AndReadsItsRecordingsInOrder()
    {
        var name = Unique("Report");
        var projectId = (await Store.CreateProjectAsync(name, Token))!.Value;
        var later = await RecordingAsync(projectId, DateTimeOffset.UtcNow.AddMinutes(-5));
        var earlier = await RecordingAsync(projectId, DateTimeOffset.UtcNow.AddMinutes(-30));
        await Store.AddRecordingFileAsync(earlier.Id, "recording.mcap", Token);
        var catalog = AnalysisCatalog.For(fixture.Server.DataSource);

        var taskId = await catalog.QueueProjectTaskAsync(projectId, AndroidProjectReportKind.Name, 1, NoProfiles, @"C:\Recordings", cancellationToken: Token);

        var task = (await catalog.TaskAsync(taskId, Token))!;
        Assert.Equal((null, projectId), (task.RecordingId, task.ProjectId));
        Assert.Throws<StepFailedException>(() => task.Recording);
        var (readName, recordings) = (await catalog.ProjectAsync(projectId, Token))!.Value;
        Assert.Equal(name, readName);
        Assert.Equal([earlier.Key, later.Key], recordings.Select(recording => recording.SessionKey));
        Assert.Equal(["recording.mcap", null], recordings.Select(recording => recording.RecordingFile));
        Assert.All(recordings, recording => Assert.False(recording.Ended));
        Assert.Null(await catalog.ProjectAsync(Guid.NewGuid(), Token));

        // A task is of a recording or of a project, never both or neither.
        await using var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token);
        foreach (var (recording, project) in new (object, object)[] { (earlier.Id, projectId), (DBNull.Value, DBNull.Value) })
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO analysis_tasks (recording_id, project_id, kind, kind_version, stage_profiles) VALUES ($1, $2, 'android-report', 1, '{}')",
                connection);
            command.Parameters.Add(new NpgsqlParameter { Value = recording, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
            command.Parameters.Add(new NpgsqlParameter { Value = project, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token));
            Assert.Equal("analysis_tasks_one_subject", failure.ConstraintName);
        }

        // Removing the project removes its tasks.
        foreach (var sql in new[]
        {
            "DELETE FROM analysis_tasks WHERE recording_id IN (SELECT recording_id FROM recordings WHERE project_id = $1)",
            "DELETE FROM recordings WHERE project_id = $1",
            "DELETE FROM projects WHERE project_id = $1",
        })
        {
            await using var remove = new NpgsqlCommand(sql, connection);
            remove.Parameters.AddWithValue(projectId);
            await remove.ExecuteNonQueryAsync(Token);
        }

        Assert.Null(await catalog.TaskAsync(taskId, Token));
    }

    private async Task<(Guid Id, string Key)> RecordingAsync(Guid projectId, DateTimeOffset started)
    {
        var key = "project-" + Guid.NewGuid().ToString("N");
        var id = await Store.CreateRecordingAsync(projectId, Definition(key) with { StartedUtc = started }, Token);
        return (id, key);
    }
}
