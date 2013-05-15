using NUnit.Framework;
using FubuCore;
using FubuTestingSupport;
using ripple.Local;
using ripple.Model;
using ripple.Runners;

namespace ripple.Testing
{
    [TestFixture]
    public class SolutionTester
    {
        [Test]
        public void create_process_info_for_full_build()
        {
            var solution = new Solution
            {
                SourceFolder = "src",
                BuildCommand = "rake",
                FastBuildCommand = "rake compile",
				Directory = "directory1".ToFullPath()
            };

            var processInfo = solution.CreateBuildProcess(false);

            processInfo.WorkingDirectory.ShouldEqual("directory1".ToFullPath());
            processInfo.FileName.ShouldEqual(Runner.Rake.Path);
            processInfo.Arguments.ShouldBeEmpty();
        }

        [Test]
        public void create_process_for_fast_build()
        {
			var solution = new Solution
			{
				SourceFolder = "src",
                BuildCommand = "rake",
				FastBuildCommand = "rake compile",
				Directory = "directory1".ToFullPath()
			};

            var processInfo = solution.CreateBuildProcess(true);

            processInfo.WorkingDirectory.ShouldEqual("directory1".ToFullPath());
            processInfo.FileName.ShouldEqual(Runner.Rake.Path);
            processInfo.Arguments.ShouldEqual("compile");
        }

        [Test]
        public void get_nuget_directory()
        {
            var solution = new Solution
            {
                SourceFolder = "source",
				Directory = ".".ToFullPath()
            };

	        var storage = new StubNugetStorage();
	        storage.Add("FubuCore", "0.9.1.37");
			solution.UseStorage(storage);

            var project = new Project("something.csproj");
            var dependency = new Dependency("FubuCore", "0.9.1.37");
            project.AddDependency(dependency);
            solution.AddProject(project);

            var spec = new NugetSpec("FubuCore", "somefile.nuspec");

            solution.NugetFolderFor(spec)
                .ShouldEqual(".".ToFullPath().AppendPath(solution.PackagesDirectory(), "FubuCore"));
        
        }

        [Test]
        public void gets_the_default_float_constraint()
        {
            var solution = new Solution();
            solution.DefaultFloatConstraint.ShouldEqual(solution.NuspecSettings.Float.ToString());
        }

        [Test]
        public void gets_the_default_fixed_constraint()
        {
            var solution = new Solution();
            solution.DefaultFloatConstraint.ShouldEqual(solution.NuspecSettings.Float.ToString());
        }

        [Test]
        public void sets_the_default_float_constraint()
        {
            var solution = new Solution();
            solution.DefaultFloatConstraint = "Current,NextMin";

            solution.NuspecSettings.Float.ToString().ShouldEqual("Current,NextMin");
        }

        [Test]
        public void sets_the_default_fixed_constraint()
        {
            var solution = new Solution();
            solution.DefaultFixedConstraint = "Current";

            solution.NuspecSettings.Fixed.ToString().ShouldEqual("Current");
        }

        [Test]
        public void uses_explicit_dependency_constraint()
        {
            var explicitDep = new Dependency("FubuCore") { Constraint = "Current,NextMin"};


            var solution = new Solution();
            solution.AddDependency(explicitDep);

            solution.ConstraintFor(explicitDep).ToString().ShouldEqual("Current,NextMin");
        }

        [Test]
        public void falls_back_to_default_constraint_for_float()
        {
            var dep = new Dependency("FubuCore", UpdateMode.Float);


            var solution = new Solution();
            solution.AddDependency(dep);

            solution.ConstraintFor(dep).ShouldEqual(solution.NuspecSettings.Float);
        }

        [Test]
        public void falls_back_to_default_constraint_for_fixed()
        {
            var dep = new Dependency("FubuCore", UpdateMode.Fixed);


            var solution = new Solution();
            solution.AddDependency(dep);

            solution.ConstraintFor(dep).ShouldEqual(solution.NuspecSettings.Fixed);
        }
    }
}