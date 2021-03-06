using System;
using System.Threading.Tasks;
using NuKeeper.Configuration;
using NuKeeper.Git;
using NuKeeper.GitHub;
using NuKeeper.Inspection.Logging;
using NuKeeper.Inspection.RepositoryInspection;
using NuKeeper.Inspection.Sources;
using NuKeeper.Update;
using Octokit;

namespace NuKeeper.Engine.Packages
{
    public class PackageUpdater : IPackageUpdater
    {
        private readonly IGitHub _gitHub;
        private readonly INuKeeperLogger _logger;
        private readonly ModalSettings _modalSettings;
        private readonly IUpdateRunner _updateRunner;

        public PackageUpdater(
            IGitHub gitHub,
            IUpdateRunner localUpdater,
            INuKeeperLogger logger,
            ModalSettings modalSettings)
        {
            _gitHub = gitHub;
            _updateRunner = localUpdater;
            _logger = logger;
            _modalSettings = modalSettings;
        }

        public async Task<bool> MakeUpdatePullRequest(
            IGitDriver git,
            PackageUpdateSet updateSet,
            NuGetSources sources,
            RepositoryData repository)
        {
            try
            {
                _logger.Minimal(UpdatesLogger.OldVersionsToBeUpdated(updateSet));

                git.Checkout(repository.DefaultBranch);

                // branch
                var branchName = BranchNamer.MakeName(updateSet);
                _logger.Detailed($"Using branch name: '{branchName}'");
                git.CheckoutNewBranch(branchName);

                await _updateRunner.Update(updateSet, sources);

                var commitMessage = CommitWording.MakeCommitMessage(updateSet);
                git.Commit(commitMessage);

                git.Push("nukeeper_push", branchName);

                var prTitle = CommitWording.MakePullRequestTitle(updateSet);
                await MakeGitHubPullRequest(updateSet, repository, prTitle, branchName);

                git.Checkout(repository.DefaultBranch);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Update failed", ex);
                return false;
            }
        }

        private async Task MakeGitHubPullRequest(
            PackageUpdateSet updates,
            RepositoryData repository,
            string title, string branchWithChanges)
        {
            string qualifiedBranch;
            if (repository.Pull.Owner == repository.Push.Owner)
            {
                qualifiedBranch = branchWithChanges;
            }
            else
            {
                qualifiedBranch = repository.Push.Owner + ":" + branchWithChanges;
            }

            var pr = new NewPullRequest(title, qualifiedBranch, repository.DefaultBranch)
            {
                Body = CommitWording.MakeCommitDetails(updates)
            };

            await _gitHub.OpenPullRequest(repository.Pull, pr, _modalSettings.Labels);
        }
    }
}
