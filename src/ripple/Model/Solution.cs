using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using FubuCore;
using FubuCore.CommandLine;
using FubuCore.Descriptions;
using FubuCore.Logging;
using ripple.Commands;
using ripple.Nuget;
using ripple.Runners;

namespace ripple.Model
{
    public interface ISolution
    {
        string NugetFolderFor(string nugetName);
        string Directory { get; }
        void IgnoreFile(string file);
        IEnumerable<string> AllNugetDependencyNames();
    }

    [XmlType("ripple")]
    public class Solution : DescribesItself, LogTopic, ISolution
    {
        private readonly IList<Project> _projects = new List<Project>();
        private readonly IList<Feed> _feeds = new List<Feed>();
        private readonly IList<Dependency> _configuredDependencies = new List<Dependency>();
        private Lazy<IEnumerable<Dependency>> _missing;
        private Lazy<IList<NugetSpec>> _specifications;
        private Lazy<DependencyCollection> _dependencies;
        private readonly IList<NugetSpec> _nugetDependencies = new List<NugetSpec>();
        private string _path;
        private string _cacheLocalPath;

        public Solution()
        {
            NugetSpecFolder = "packaging/nuget";
            SourceFolder = "src";
            BuildCommand = "rake";
            FastBuildCommand = "rake compile";
            Mode = SolutionMode.Ripple;
            Groups = new List<DependencyGroup>();
            Nuspecs = new List<NuspecMap>();

            AddFeed(Feed.Fubu);
            AddFeed(Feed.NuGetV2);

            UseStorage(NugetStorage.Basic());
            UseFeedService(Model.FeedService.Basic(this));
            UseCache(NugetFolderCache.DefaultFor(this));
            UsePublisher(PublishingService.Basic());
            UseBuilder(new NugetPlanBuilder());

            RestoreSettings = new RestoreSettings();
            NuspecSettings = new NuspecSettings();
            References = new ReferenceSettings();

            Reset();
        }

        public string Name { get; set; }
        public string NugetSpecFolder { get; set; }
        public string SourceFolder { get; set; }
        public string BuildCommand { get; set; }
        public string FastBuildCommand { get; set; }

        public string NugetCacheDirectory
        {
            get { return _cacheLocalPath; }
            set
            {
                if (value.IsNotEmpty())
                {
                    _cacheLocalPath = value;
                    UseCache(NugetFolderCache.DefaultFor(this));
                }
            }
        }

        public string DefaultFloatConstraint
        {
            get { return NuspecSettings.Float.ToString(); }
            set
            {
                if (value.IsNotEmpty())
                {
                    NuspecSettings.Float = VersionConstraint.Parse(value);
                }
            }
        }

        public string DefaultFixedConstraint
        {
            get { return NuspecSettings.Fixed.ToString(); }
            set
            {
                if (value.IsNotEmpty())
                {
                    NuspecSettings.Fixed = VersionConstraint.Parse(value);
                }
            }
        }

        public Feed[] Feeds
        {
            get { return _feeds.ToArray(); }
            set
            {
                _feeds.Clear();
                _feeds.AddRange(value);
            }
        }

        public Dependency[] Nugets
        {
            get { return _configuredDependencies.OrderBy(x => x.Name).ToArray(); }
            set
            {
                _configuredDependencies.Clear();
                _configuredDependencies.AddRange(value.OrderBy(x => x.Name));
                resetDependencies();
            }
        }

        [XmlArray("Groups")]
        [XmlArrayItem("Group")]
        public List<DependencyGroup> Groups { get; set; }

        [XmlElement("References")]
        public ReferenceSettings References { get; set; }

        [XmlArray("Nuspecs")]
        [XmlArrayItem("Nuspec")]
        public List<NuspecMap> Nuspecs { get; set; }

        [XmlIgnore]
        public DependencyCollection Dependencies
        {
            get { return _dependencies.Value; }
        }

        [XmlIgnore]
        public IEnumerable<NugetSpec> Specifications
        {
            get { return _specifications.Value; }
        }

        [XmlIgnore]
        public Project[] Projects
        {
            get { return _projects.ToArray(); }
            set
            {
                _projects.Clear();
                _projects.AddRange(value);
            }
        }

        [XmlIgnore]
        public string Directory { get; set; }
        [XmlIgnore]
        public SolutionMode Mode { get; set; }
        [XmlIgnore]
        public INugetStorage Storage { get; private set; }
        [XmlIgnore]
        public IFeedService FeedService { get; private set; }
        [XmlIgnore]
        public INugetCache Cache { get; private set; }
        [XmlIgnore]
        public IPublishingService Publisher { get; private set; }
        [XmlIgnore]
        public INugetPlanBuilder Builder { get; private set; }
        [XmlIgnore]
        public RestoreSettings RestoreSettings { get; private set; }
        [XmlIgnore]
        public NuspecSettings NuspecSettings { get; private set; }

        [XmlIgnore]
        public string Path
        {
            get { return _path; }
            set
            {
                _path = value;
                if (value.IsNotEmpty() && File.Exists(value))
                {
                    Directory = System.IO.Path.GetDirectoryName(value);
                }
            }
        }

        private void resetDependencies()
        {
            _dependencies = new Lazy<DependencyCollection>(combineDependencies);
        }

        private DependencyCollection combineDependencies()
        {
            var dependencies = new DependencyCollection(_configuredDependencies);
            Projects.Each(p => dependencies.AddChild(p.Dependencies));
            return dependencies;
        }

        private IList<NugetSpec> findSpecifications()
        {
            var specs = new List<NugetSpec>();
            specs.AddRange(Publisher.SpecificationsFor(this));

            return specs;
        }

        public void AddFeed(Feed feed)
        {
            _feeds.Fill(feed);
        }

        public void AddFeeds(IEnumerable<Feed> feeds)
        {
            _feeds.Fill(feeds);
        }

        public void AddProject(Project project)
        {
            project.Solution = this;
            _projects.Fill(project);
        }

        public Project AddProject(string name)
        {
            var project = new Project(name);
            AddProject(project);

            return project;
        }

        public void AddDependency(Dependency dependency)
        {
            resetDependencies();
            if (_configuredDependencies.Any(x => x.MatchesName(dependency)))
                return;
            _configuredDependencies.Fill(dependency);
        }

        public void RemoveDependency(string name)
        {
            var dep = _configuredDependencies.SingleOrDefault(x => x.MatchesName(name));
            if (dep != null)
            {
                _configuredDependencies.Remove(dep);
                resetDependencies();
            }
        }

        public IEnumerable<string> AllNugetDependencyNames()
        {
            return Dependencies.Select(x => x.Name);
        }

        public ValidationResult Validate()
        {
            var localDependencies = LocalDependencies();
            var result = new ValidationResult(this);

            Dependencies.Each(dependency =>
            {
                if (!localDependencies.Has(dependency))
                {
                    result.AddProblem(dependency.Name, "Not found");
                    return;
                }

                var local = localDependencies.Get(dependency);
                if (dependency.Version.IsNotEmpty() && local.Version < dependency.SemanticVersion())
                {
                    result.AddProblem(dependency.Name, "Solution requires {0} but the local copy is {1}".ToFormat(dependency.Version, local.Version.ToString()));
                }
            });

            return result;
        }

        public void AssertIsValid()
        {
            var result = Validate();
            if (result.IsValid())
            {
                RippleLog.Info("Solution valid");
                return;
            };

            RippleLog.InfoMessage(result);
            RippleAssert.Fail("Validation failed");
        }

        public void ConvertTo(SolutionMode mode)
        {
            Mode = mode;
            Storage.Reset(this);
            UseStorage(NugetStorage.For(mode));
        }

        public Dependency FindDependency(string name)
        {
            return _configuredDependencies.SingleOrDefault(x => x.MatchesName(name));
        }

        public void UpdateDependency(Dependency dependency)
        {
            var existing = FindDependency(dependency.Name);
            _configuredDependencies.Remove(existing);

            AddDependency(dependency);
        }

        public void ClearFeeds()
        {
            _feeds.Clear();
        }

        public void Clean(CleanMode mode)
        {
            Storage.Clean(this, mode);
        }

        public void DetermineNugetDependencies(Func<string, NugetSpec> finder)
        {
            Dependencies.Each(x =>
            {
                var spec = finder(x.Name);
                if (spec != null)
                {
                    _nugetDependencies.Add(spec);
                }
            });
        }

        public void EachProject(Action<Project> action)
        {
            Projects.Each(action);
        }

        public IEnumerable<Dependency> MissingNugets()
        {
            return _missing.Value;
        }

        public string PackagesDirectory()
        {
            return Directory.AppendPath(SourceFolder, "packages").ToFullPath();
        }

        public Task<NugetResult> Restore(Dependency dependency)
        {
            return FeedService.NugetFor(dependency);
        }

        public Project FindProject(string name)
        {
            return _projects.SingleOrDefault(x => x.Name.EqualsIgnoreCase(name));
        }

        public void IgnoreFile(string file)
        {
            var gitIgnoreFile = Directory.AppendPath(".gitignore");
            new FileSystem().AlterFlatFile(gitIgnoreFile, list => list.Fill(file));
        }

        public void UsePublisher(IPublishingService service)
        {
            Publisher = service;
        }

        public void UseStorage(INugetStorage storage)
        {
            Storage = storage;
        }

        public void UseFeedService(IFeedService service)
        {
            FeedService = service;
        }

        public void UseCache(INugetCache cache)
        {
            Cache = cache;
        }

        public void UseBuilder(INugetPlanBuilder builder)
        {
            Builder = builder;
        }

        public void Describe(Description description)
        {
            description.Title = "Solution \"{0}\"".ToFormat(Name);
            description.ShortDescription = Path;

            var configured = description.AddList("SolutionLevel", _configuredDependencies.OrderBy(x => x.Name));
            configured.Label = "Solution-Level";

            var feedsList = description.AddList("Feeds", Feeds);
            feedsList.Label = "NuGet Feeds";

            var projectsList = description.AddList("Projects", Projects);
            projectsList.Label = "Projects";

            var local = LocalDependencies();
            if (local.Any())
            {
                var localList = description.AddList("Local", local.All());
                localList.Label = "Local";
            }

            var missing = MissingNugets();
            if (missing.Any())
            {
                var missingList = description.AddList("Missing", missing);
                missingList.Label = "Missing";
            }
        }

        public void ForceRestore()
        {
            RestoreSettings.ForceAll();
        }

        public void ForceRestore(string name)
        {
            RestoreSettings.Force(name);
        }

        // Mostly for testing
        public void Reset()
        {
            _missing = new Lazy<IEnumerable<Dependency>>(() => Storage.MissingFiles(this));
            _specifications = new Lazy<IList<NugetSpec>>(findSpecifications);

            resetDependencies();
        }

        public bool HasLocalCopy(string name)
        {
            return LocalDependencies().Has(name);
        }

        public IRemoteNuget LocalNuget(string name)
        {
            var file = LocalDependencies().Get(name);
            return new FileSystemNuget(file);
        }

        public LocalDependencies LocalDependencies()
        {
            return Storage.Dependencies(this);
        }

        public void Update(INugetFile nuget)
        {
            Dependencies.Update(Dependency.For(nuget));
        }

        public string Package(PackageParams ctx)
        {
            return Publisher.CreatePackage(ctx);
        }

        public string NugetFolderFor(NugetSpec spec)
        {
            return NugetFolderFor(spec.Name);
        }

        public string NugetFolderFor(string nugetName)
        {
            var nuget = LocalDependencies().Get(nugetName);
            return nuget.NugetFolder(this);
        }

        public void AddNugetSpec(NugetSpec spec)
        {
            _specifications.Value.Add(spec);
        }

        public IEnumerable<NugetSpec> NugetDependencies
        {
            get { return _nugetDependencies; }
        }

        public IEnumerable<Solution> SolutionDependencies()
        {
            return _nugetDependencies.Select(x => x.Publisher)
              .Distinct()
              .OrderBy(x => x.Name);
        }

        public bool HasLockedFiles()
        {
            return LocalDependencies().HasLockedFiles(this);
        }

        public void AssertNoLockedFiles()
        {
            if (!HasLockedFiles()) return;

            if (Process.GetProcessesByName("devenv.exe").Any())
            {
                RippleAssert.Fail("Detected locked files. Do you have Visual Studio open?");
                return;
            }

            RippleAssert.Fail("Detected locked files. Exiting.");
        }

        public VersionConstraint ConstraintFor(Dependency dependency)
        {
            if (dependency.VersionConstraint != null)
            {
                return dependency.VersionConstraint;
            }

            return NuspecSettings.ConstraintFor(dependency.Mode);
        }

        public void Save(bool force = false)
        {
            Storage.Write(this);
            Projects.Where(x => force || x.HasChanges()).Each(Storage.Write);
        }

        public ProcessStartInfo CreateBuildProcess(bool fast)
        {
            var cmdLine = fast ? FastBuildCommand : BuildCommand;
            var commands = StringTokenizer.Tokenize(cmdLine);

            var fileName = commands.First();
            ProcessStartInfo info;
            if (fileName == "rake")
            {
                info = Runner.Rake.Info(commands.Skip(1).Join(" "));
            }
            else
            {
                info = new ProcessStartInfo(fileName);
            }

            info.WorkingDirectory = Directory;
            return info;
        }

        public override string ToString()
        {
            return "{0} ({1})".ToFormat(Name, Directory);
        }

        public static Solution Empty()
        {
            var solution = new Solution();
            solution.ClearFeeds();

            return solution;
        }

        public static Solution NuGet(string name)
        {
            var solution = new Solution { Name = name };

            solution.ClearFeeds();
            solution.AddFeed(Feed.NuGetV2);

            return solution;
        }

        public static Solution For(RippleInput input)
        {
            return SolutionBuilder.ReadFromCurrentDirectory();
        }
    }
}