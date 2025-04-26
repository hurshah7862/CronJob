using System.Globalization;
using Cronos;

class Program
{
    static async Task Main(string[] args)
    {
        // Start background task to run cron jobs
        Task.Run(() => CronJobManager.RunAllJobsAsync());

        while (true)
        {
            Console.WriteLine("\nChoose an option:");
            Console.WriteLine("Press:1 Add a new cron job");
            Console.WriteLine("Press:2 Edit a cron job");
            Console.WriteLine("Press:3 Delete a cron job");
            Console.WriteLine("Press:4 List all cron jobs");
            Console.WriteLine("Press:5 Stop the job");
            Console.WriteLine("Press:6 Restart the job");
            Console.WriteLine("Press:7 Exit");
            Console.Write("Enter your choice: ");
            Console.Write("\n");
            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    Console.Write("Enter URL: ");
                    var url = Console.ReadLine()?.Trim();
                    Console.Write("Enter Cron Expression (e.g., '*/5 * * * *'): ");
                    var cron = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(cron))
                        CronJobManager.AddJob(url, cron);
                    else
                        Console.WriteLine("Invalid input.");
                    break;

                case "2":
                    Console.Write("Enter Job ID to edit: ");
                    if (int.TryParse(Console.ReadLine(), out int editId))
                    {
                        var job = CronJobManager.GetJobById(editId);
                        if (job == null)
                        {
                            Console.WriteLine("Invalid ID.Please try with valid ID");
                            break;
                        }
                        Console.Write("New URL: ");
                        var newUrl = Console.ReadLine()?.Trim();
                        Console.Write("New Cron Expression: ");
                        var newCron = Console.ReadLine()?.Trim();
                        CronJobManager.EditJob(editId, newUrl, newCron);
                    }
                    else
                    {
                        Console.WriteLine("Invalid ID.");
                    }
                    break;

                case "3":
                    Console.Write("Enter Job ID to delete: ");
                    if (int.TryParse(Console.ReadLine(), out int deleteId))
                        CronJobManager.DeleteJob(deleteId);
                    else
                        Console.WriteLine("Invalid ID.");
                    break;

                case "4":
                    CronJobManager.ListJobs();
                    break;
                case "5": // Case for stopping a job
                    Console.Write("Enter Job ID to stop: ");
                    if (int.TryParse(Console.ReadLine(), out int jobId))
                        CronJobManager.StopJob(jobId);
                    else
                        Console.WriteLine("Invalid Job ID.");
                    break;
                case "6":
                    Console.Write("Enter Job ID to start: ");
                    if (int.TryParse(Console.ReadLine(), out int startJobId))
                    {
                        CronJobManager.StartJob(startJobId);
                    }
                    else
                    {
                        Console.WriteLine("Invalid input.");
                    }
                    break;
                case "7":
                    Console.WriteLine("Exiting...");
                    return;

                default:
                    Console.WriteLine("Invalid choice.");
                    break;
            }
        }
    }
}

class CronJob
{
    public int Id { get; set; }
    public string Url { get; set; }
    public string CronExpression { get; set; }
    public DateTime Added { get; set; }
    public Task? RunningTask { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();
    public string Status { get; set; } = "Scheduled";
    public DateTime? StartTime { get; set; }
    public DateTime? StoppedTime { get; set; }
    public DateTime? EndedTime { get; set; }
}
class CronJobManager
{
    private static readonly List<CronJob> _jobs = new();
    private static int _jobIdCounter = 1;
    private static readonly object _lock = new(); // Prevent concurrency issues
    public static void LoadJobsFromFile()
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string filePath = Path.Combine(desktopPath, "JobDetails.txt");

        if (!File.Exists(filePath)) return;

        var lines = File.ReadAllLines(filePath);

        lock (_lock)
        {
            _jobs.Clear();

            foreach (var line in lines)
            {
                var job = ParseJobLine(line);
                if (job != null) _jobs.Add(job);
            }
        }
    }

    private static CronJob ParseJobLine(string line)
    {
        var job = new CronJob();
        var segments = line.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var colonIndex = segment.IndexOf(':');
            if (colonIndex == -1) continue;

            var key = segment.Substring(0, colonIndex).Trim();
            var value = segment.Substring(colonIndex + 1).Trim();

            switch (key)
            {
                case "Job ID":
                    if (int.TryParse(value, out var id)) job.Id = id;
                    break;
                case "URL":
                    // Handle duplicate URL prefix
                    if (value.StartsWith("URL: "))
                        value = value.Substring("URL: ".Length);
                    job.Url = value;
                    break;
                case "Cron":
                    // Handle duplicate Cron prefix
                    if (value.StartsWith("Cron: "))
                        value = value.Substring("Cron: ".Length);
                    job.CronExpression = value;
                    break;
                case "Added":
                    if (DateTime.TryParseExact(value, "M/d/yyyy h:mm:ss tt",
                           CultureInfo.InvariantCulture, DateTimeStyles.None, out var added))
                        job.Added = added;
                    break;
                case "StartTime":
                    if (value != "null" && DateTime.TryParseExact(value, "M/d/yyyy h:mm:ss tt",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                        job.StartTime = start;
                    break;
                case "StoppedTime":
                    if (value != "null" && DateTime.TryParseExact(value, "M/d/yyyy h:mm:ss tt",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var stopped))
                        job.StoppedTime = stopped;
                    break;
                case "EndedTime":
                    if (value != "null" && DateTime.TryParseExact(value, "M/d/yyyy h:mm:ss tt",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var ended))
                        job.EndedTime = ended;
                    break;
            }
        }

        // Validate required fields
        if (job.Id == 0 ||
            string.IsNullOrEmpty(job.Url) ||
            string.IsNullOrEmpty(job.CronExpression) ||
            job.Added == default)
        {
            return null;
        }

        // Initialize CTS for existing jobs
        job.CancellationTokenSource = new CancellationTokenSource();
        job.Status = "Scheduled";  // Reset status when loading from file

        return job;
    }

    public static CronJob? GetJobById(int jobId)
    {
        lock (_lock) // Ensure thread safety
        {
            return _jobs.FirstOrDefault(job => job.Id == jobId);
        }
    }
    public static void StopJob(int jobId)
    {
        lock (_lock) // Ensure thread safety
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null)
            {
                job.CancellationTokenSource.Cancel(); // Signal cancellation
                job.CancellationTokenSource.Dispose(); // Dispose of the token source
                job.Status = "Stopped";
                Console.WriteLine($"Job {job.Id} has been stopped.");
            }
            else
            {
                Console.WriteLine($"Job with ID {jobId} not found.");
            }
        }
    }

    public static void AddJob(string url, string cronExpression)
    {
        lock (_lock)
        {
            // Generate a unique Job ID
            int newId = _jobs.Count > 0 ? _jobs.Max(j => j.Id) + 1 : 1;

            var job = new CronJob
            {
                Id = newId,
                Url = url,
                CronExpression = cronExpression,
                Added = DateTime.Now, // Set Added to current time
                                      // StartTime, StoppedTime, EndedTime remain null by default
                CancellationTokenSource = new CancellationTokenSource()
            };

            _jobs.Add(job);
            Console.WriteLine($"Job {job.Id} added.");
            SaveJobDetailsToFile(job); // Save to file with all fields
        }
    }

    public static void EditJob(int jobId, string newUrl, string newCronExpression)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null)
            {
                Console.WriteLine("Job not found.");
                return;
            }

            job.CancellationTokenSource.Cancel();
            job.Url = newUrl;
            job.CronExpression = newCronExpression;
            job.CancellationTokenSource = new CancellationTokenSource();
            Console.WriteLine($" Job {job.Id} updated.");
            // Write job details to a text file on the desktop
            SaveJobDetailsToFile(job);
        }
    }
    public static void DeleteJob(int jobId)
    {
        lock (_lock)
        {
            var jobToRemove = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (jobToRemove == null) return;

            try
            {
                // Cancel the job if it's running
                if (!jobToRemove.CancellationTokenSource.IsCancellationRequested)
                {
                    jobToRemove.CancellationTokenSource.Cancel();
                    jobToRemove.Status = "Stopped";
                    jobToRemove.StoppedTime = DateTime.UtcNow;
                }

                _jobs.Remove(jobToRemove);
                UpdateJobFile();
                LogToFile($"Job {jobId} deleted successfully");
            }
            catch (Exception ex)
            {
                LogToFile($"Error deleting job {jobId}: {ex.Message}");
            }
        }
    }

    private static void UpdateJobFile()
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string filePath = Path.Combine(desktopPath, "JobDetails.txt");

        var newLines = new List<string>();
        foreach (var job in _jobs)
        {
            newLines.Add(FormatJobLine(job));
        }

        // Write all remaining jobs to file
        File.WriteAllLines(filePath, newLines);
    }

    private static string FormatJobLine(CronJob job)
    {
        return string.Format(
            "Job ID: {0}, URL: {1}, Cron: {2}, Added: {3}, " +
            "StartTime: {4}, StoppedTime: {5}, EndedTime: {6}",
            job.Id,
            job.Url,
            job.CronExpression,
            job.Added.ToString("M/d/yyyy h:mm:ss tt"),
            job.StartTime?.ToString("M/d/yyyy h:mm:ss tt") ?? "null",
            job.StoppedTime?.ToString("M/d/yyyy h:mm:ss tt") ?? "null",
            job.EndedTime?.ToString("M/d/yyyy h:mm:ss tt") ?? "null"
        );
    }

    public static void ListJobs()
    {
        LoadJobsFromFile();
        lock (_lock)
        {

            Console.WriteLine("Active Cron Jobs:");
            foreach (var job in _jobs)
            {
                Console.WriteLine($"ID: {job.Id}, URL: {job.Url}, Cron: {job.CronExpression}, Status: {job.Status}");
            }
            if (_jobs.Count == 0)
            {
                Console.WriteLine("There is no Cron Job present please add them first");
            }
        }
    }
    public static void StartJob(int jobId)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null)
            {
                if (job.Status == "Stopped")
                {
                    // Replace the disposed CTS with a new one
                    job.CancellationTokenSource = new CancellationTokenSource();
                    job.Status = "Scheduled";

                    Console.WriteLine($"Restarting Job {job.Id}...");

                    // Start the job with the new CTS
                    Task.Run(async () => await RunJobAsync(job), job.CancellationTokenSource.Token);

                    Console.WriteLine($"Job {job.Id} has been restarted.");
                }
                else
                {
                    Console.WriteLine($"Job {job.Id} is already running.");
                }
            }
            else
            {
                Console.WriteLine($"Job with ID {jobId} not found.");
            }
        }
    }

    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = Timeout.InfiniteTimeSpan // Disables timeout
    };

    private static void LogToFile(string message)
    {
        string logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "JobLogs.txt");
        File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}\n");
    }
    public static async Task RunAllJobsAsync()
    {
        LoadJobsFromFile();

        while (true)
        {
            lock (_lock)
            {
                foreach (var job in _jobs)
                {
                    // Only start jobs that aren't already running/completed
                    if (job.RunningTask == null &&
                        job.Status is "Scheduled" or "Failed" &&
                        !job.CancellationTokenSource.IsCancellationRequested)
                    {
                        job.RunningTask = RunJobAsync(job);
                    }
                }
            }

            await Task.Delay(5000); // Check every 5 seconds
        }
    }

    private static async Task RunJobAsync(CronJob job)
    {
        var cronSchedule = CronExpression.Parse(job.CronExpression);

        try
        {
            while (!job.CancellationTokenSource.Token.IsCancellationRequested)
            {
                var nextRun = cronSchedule.GetNextOccurrence(DateTime.UtcNow);
                if (!nextRun.HasValue) break;

                var delay = nextRun.Value - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    LogToFile($"Job {job.Id} next run at {nextRun.Value}");
                    //await Task.Delay(delay, job.CancellationTokenSource.Token);
                }

                if (job.CancellationTokenSource.Token.IsCancellationRequested)
                    break;

                // Update and save START TIME
                job.StartTime = DateTime.UtcNow;
                job.Status = "Running";
                SaveJobDetailsToFile(job); // Immediate save on start
                LogToFile($"Job {job.Id} started execution");

                try
                {
                    await HitUrlAsync(job.Url, job);

                    // Update and save END TIME
                    job.Status = "Completed";
                    job.EndedTime = DateTime.UtcNow;
                    SaveJobDetailsToFile(job);
                    LogToFile($"Job {job.Id} completed successfully");
                }
                catch (Exception ex)
                {
                    // Update and save FAILURE TIME
                    job.Status = "Failed";
                    job.EndedTime = DateTime.UtcNow;
                    SaveJobDetailsToFile(job);
                    LogToFile($"Job {job.Id} failed: {ex.Message}");
                }
            }
        }
        catch (TaskCanceledException)
        {
            // Update and save STOPPED TIME
            job.StoppedTime = DateTime.UtcNow;
            job.Status = "Stopped";
            SaveJobDetailsToFile(job);
            LogToFile($"Job {job.Id} was cancelled");
        }
    }

    private static void SaveJobDetailsToFile(CronJob job)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string filePath = Path.Combine(desktopPath, "JobDetails.txt");

        var lines = File.Exists(filePath)
            ? File.ReadAllLines(filePath).ToList()
            : new List<string>();

        // Remove existing entry if present
        lines.RemoveAll(l => l.StartsWith($"Job ID: {job.Id},"));

        // Add updated job entry
        lines.Add($""""
        Job ID: {job.Id}, 
        URL: {job.Url}, 
        Cron: {job.CronExpression}, 
        Added: {job.Added.ToString("M/d/yyyy h:mm:ss tt")}, 
        StartTime: {job.StartTime?.ToString("M/d/yyyy h:mm:ss tt") ?? "null"}, 
        StoppedTime: {job.StoppedTime?.ToString("M/d/yyyy h:mm:ss tt") ?? "null"}, 
        EndedTime: {job.EndedTime?.ToString("M/d/yyyy h:mm:ss tt") ?? "null"}
    """".ReplaceLineEndings(" ").Replace("  ", " ").Trim());

        File.WriteAllLines(filePath, lines);
    }

    private static async Task HitUrlAsync(string url, CronJob job)
    {
        try
        {
            string logMessage = $"Sending GET request to {url} at {DateTime.UtcNow}";
            LogToFile(logMessage); // Log to file

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(job.CancellationTokenSource.Token);

            var response = await _httpClient.GetAsync(url, linkedCts.Token);

            logMessage = $"Job {job.Id} Response: {response.StatusCode}";
            LogToFile(logMessage);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                logMessage = $"Job {job.Id} Response Content: {responseContent}";
                LogToFile(logMessage);
            }
            else
            {
                logMessage = $"Job {job.Id} failed with status code: {response.StatusCode}";
                LogToFile(logMessage);
            }
        }
        catch (TaskCanceledException)
        {
            LogToFile($"Job {job.Id} request was canceled.");
        }
        catch (Exception ex)
        {
            LogToFile($"Error in Job {job.Id}: {ex.Message}");
            throw;
        }
    }
}
