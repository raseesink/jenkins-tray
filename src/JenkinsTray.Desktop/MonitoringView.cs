using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using JenkinsTray.Core;

namespace JenkinsTray.Desktop;

public sealed class MonitoringView : StackPanel
{
    private readonly Action<string> openUrl;
    public MonitoringView(Action<string> openUrl)
    {
        this.openUrl = openUrl; Spacing = 12; Margin = new Thickness(16);
    }
    public AggregateStatus Update(Settings settings, ImmutableArray<ProjectSnapshot> snapshots)
    {
        Children.Clear();
        var selected = new List<ProjectSnapshot>();
        foreach (var server in settings.Servers)
        {
            var group = new StackPanel { Spacing = 8 };
            group.Children.Add(new TextBlock { Text = server.Label, FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold });
            foreach (var project in server.Projects)
            {
                var snapshot = snapshots.FirstOrDefault(p => p.ServerId == server.Id && p.Project.Url == project.Url)
                    ?? new(server.Id, project, BuildResult.Unknown, false, false, null, null, null, "Waiting for first refresh.");
                selected.Add(snapshot);
                var row = new StackPanel { Spacing = 4 };
                var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                heading.Children.Add(App.Button(project.DisplayName ?? project.Name, () => openUrl(project.Url)));
                heading.Children.Add(new TextBlock { Text = snapshot.Result + (snapshot.Building ? " · Building" : "") + (snapshot.Queued ? " · Queued" : "") + (!snapshot.Available ? " · Unavailable / stale" : ""), VerticalAlignment = VerticalAlignment.Center });
                if (snapshot.ConsoleUrl is { } console) heading.Children.Add(App.Button("Console", () => openUrl(console)));
                row.Children.Add(heading);
                if (snapshot.LatestBuild is { } build)
                    row.Children.Add(new TextBlock { Text = $"{build.DisplayName ?? "Build #" + build.Number}   {build.Timestamp?.ToLocalTime():g}   {build.Duration?.TotalSeconds:0.#} seconds", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                if (snapshot.Error is { } error) row.Children.Add(new TextBlock { Text = error + (snapshot.RefreshedAt is { } time ? $" Last updated {time.ToLocalTime():g}." : ""), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                group.Children.Add(row);
            }
            if (server.Projects.IsEmpty) group.Children.Add(new TextBlock { Text = "No projects selected. Open Settings to discover projects." });
            Children.Add(group);
        }
        if (selected.Count == 0) Children.Add(new TextBlock { Text = "No projects selected. Open Settings to configure monitoring." });
        return StatusPolicy.Aggregate(selected);
    }
}
