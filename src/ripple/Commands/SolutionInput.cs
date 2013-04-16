using System;
using System.Collections.Generic;
using System.ComponentModel;
using FubuCore;
using FubuCore.CommandLine;
using ripple.Model;
using ripple.Nuget;

namespace ripple.Commands
{
	public interface IOverrideFeeds
	{
		IEnumerable<Feed> Feeds();
	}

	public class SolutionInput
	{
		private readonly Lazy<SolutionGraph> _graph = new Lazy<SolutionGraph>(SolutionGraphBuilder.BuildForCurrentDirectory);

		[Description("override the solution to be cleaned")]
		[FlagAlias("solution", 'l')]
		public string SolutionFlag { get; set; }

		[Description("Override the NuGet cache")]
		[FlagAlias("cache", 'c')]
		public string CacheFlag { get; set; }

		[Description("Apply restore to all solutions")]
		public bool AllFlag { get; set; }

		[Description("Writes all output to the screen")]
		public bool VerboseFlag { get; set; }

		public void EachSolution(Action<Solution> configure)
		{
		    var solutions = FindSolutions();
		    solutions.Each(solution =>
		    {
                RippleLog.Debug("Solution " + solution.Name);
		        configure(solution);
		    });
		}

		public IEnumerable<Solution> FindSolutions()
		{
			if (SolutionFlag.IsNotEmpty())
			{
				yield return _graph.Value[SolutionFlag];
			}
			else if (AllFlag || RippleFileSystem.IsCodeDirectory())
			{
				foreach (var solution in _graph.Value.AllSolutions)
				{
					yield return solution;
				}
			}
			else
			{
				if (RippleFileSystem.IsSolutionDirectory())
				{
				    yield return SolutionBuilder.ReadFromCurrentDirectory();
				}
			}
		}

		public void Apply(Solution solution)
		{
			RippleLog.Verbose(VerboseFlag);

			if (CacheFlag.IsNotEmpty())
			{
				solution.UseCache(new NugetFolderCache(solution, CacheFlag.ToFullPath()));
			}

			ApplyTo(solution);
		}

		public virtual void ApplyTo(Solution solution)
		{
		}

		public virtual string DescribePlan(Solution solution)
		{
			return string.Empty;
		}
	}
}