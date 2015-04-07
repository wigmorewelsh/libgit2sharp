﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibGit2Sharp.Core;
using LibGit2Sharp.Tests.TestHelpers;
using Xunit;
using Xunit.Extensions;

namespace LibGit2Sharp.Tests
{
    public class RebaseFixture : BaseFixture
    {
        const string masterBranch1Name = "M1";
        const string masterBranch2Name = "M2";
        const string topicBranch1Name = "T1";
        const string topicBranch2Name = "T2";
        const string conflictBranch1Name = "C1";

        [Theory]
        [InlineData(topicBranch2Name, topicBranch2Name, topicBranch1Name, masterBranch1Name, 3)]
        [InlineData(topicBranch2Name, topicBranch2Name, topicBranch1Name, topicBranch1Name, 3)]
        [InlineData(topicBranch2Name, topicBranch1Name, masterBranch2Name, masterBranch2Name, 3)]
        [InlineData(topicBranch2Name, topicBranch1Name, masterBranch2Name, null, 3)]
        [InlineData(topicBranch1Name, null, masterBranch2Name, null, 3)]
        public void CanRebase(string initialBranchName,
                              string branchName,
                              string upstreamName,
                              string ontoName,
                              int stepCount)
        {
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();
            var path = Repository.Init(scd.DirectoryPath);
            using (Repository repo = new Repository(path))
            {
                ConstructRebaseTestRepository(repo);

                repo.Checkout(initialBranchName);
                Assert.False(repo.RetrieveStatus().IsDirty);

                Branch branch = (branchName == null) ? null : repo.Branches[branchName];
                Branch upstream = repo.Branches[upstreamName];
                Branch onto = (ontoName == null) ? null : repo.Branches[ontoName];
                Commit expectedSinceCommit = (branch == null) ? repo.Head.Tip : branch.Tip;
                Commit expectedUntilCommit = upstream.Tip;
                Commit expectedOntoCommit = (onto == null) ? upstream.Tip : onto.Tip;

                int beforeStepCallCount = 0;
                int afterStepCallCount = 0;

                List<ObjectId> PreRebaseCommits = new List<ObjectId>();
                List<ObjectId> PostRebaseCommits = new List<ObjectId>();
                ObjectId expectedParentId = upstream.Tip.Id;

                RebaseOptions options = new RebaseOptions()
                {
                    RebaseStepStarting =  x => 
                    {
                        beforeStepCallCount++;
                        PreRebaseCommits.Add(x.StepInfo.ID);
                    },
                    RebaseStepCompleted = x =>
                    {
                        afterStepCallCount++;
                        PostRebaseCommits.Add(x.CommitId);
                    },
                };

                RebaseResult rebaseResult = repo.Rebase.Start(branch, upstream, onto, Constants.Signature, options);

                // Validation:
                Assert.Equal(RebaseStatus.Complete, rebaseResult.Status);
                Assert.Equal(stepCount, rebaseResult.TotalStepCount);
                Assert.Null(rebaseResult.CurrentStepInfo);

                Assert.Equal(stepCount, rebaseResult.CompletedStepCount);
                Assert.False(repo.RetrieveStatus().IsDirty);

                Assert.Equal(stepCount, beforeStepCallCount);
                Assert.Equal(stepCount, afterStepCallCount);

                // Verify the chain of source commits that were rebased.
                CommitFilter sourceCommitFilter = new CommitFilter()
                {
                    Since = expectedSinceCommit,
                    Until = expectedUntilCommit,
                    SortBy = CommitSortStrategies.Reverse | CommitSortStrategies.Topological,
                };
                Assert.Equal(repo.Commits.QueryBy(sourceCommitFilter).Select(c => c.Id), PreRebaseCommits);

                // Verify the chain of commits that resulted from the rebase.
                Commit expectedParent = expectedOntoCommit;
                foreach(Commit rebasedCommit in PostRebaseCommits.Select(id => repo.Lookup<Commit>(id)))
                {
                    Assert.Equal(expectedParent.Id, rebasedCommit.Parents.First().Id);
                    expectedParent = rebasedCommit;
                }

                Assert.Equal(repo.Head.Tip.Id, PostRebaseCommits.Last());
            }
        }

        private class rebaseStepInfo
        {
            public Commit Commit { get; set; }
        }

        /// <summary>
        /// Verify a single rebase, but in more detail.
        /// </summary>
        [Fact]
        public void VerifyRebaseDetailed()
        {
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();
            var path = Repository.Init(scd.DirectoryPath);

            using (Repository repo = new Repository(path))
            {
                ConstructRebaseTestRepository(repo);

                Branch initialBranch = repo.Branches[topicBranch1Name];
                Branch upstreamBranch = repo.Branches[masterBranch2Name];

                repo.Checkout(initialBranch);
                Assert.False(repo.RetrieveStatus().IsDirty);

                bool wasCheckoutProgressCalled = false;
                bool wasCheckoutNotifyCalled = false;

                RebaseOptions options = new RebaseOptions()
                {
                    OnCheckoutProgress = (x, y, z) => wasCheckoutProgressCalled = true,
                    OnCheckoutNotify = (x, y) => { wasCheckoutNotifyCalled = true; return true; },
                    CheckoutNotifyFlags = CheckoutNotifyFlags.Updated,
                };

                repo.Rebase.Start(null, upstreamBranch, null, Constants.Signature2, options);

                Assert.Equal(true, wasCheckoutNotifyCalled);
                Assert.Equal(true, wasCheckoutProgressCalled);

                // Verify the chain of resultant rebased commits.
                CommitFilter commitFilter = new CommitFilter()
                {
                    Since = repo.Head.Tip,
                    Until = upstreamBranch.Tip,
                    SortBy = CommitSortStrategies.Reverse | CommitSortStrategies.Topological,
                };

                List<ObjectId> expectedTreeIds = new List<ObjectId>()
                {
                    new ObjectId("447bad85bcc1882037848370620a6f88e8ee264e"),
                    new ObjectId("3b0fc846952496a64b6149064cde21215daca8f8"),
                    new ObjectId("a2d114246012daf3ef8e7ccbfbe91889a24e1e60"),
                };

                List<Commit> rebasedCommits = repo.Commits.QueryBy(commitFilter).ToList();

                Assert.Equal(3, rebasedCommits.Count);
                for(int i = 0; i < 3; i++)
                {
                    Assert.Equal(expectedTreeIds[i], rebasedCommits[i].Tree.Id);
                    Assert.Equal(Constants.Signature.Name, rebasedCommits[i].Author.Name);
                    Assert.Equal(Constants.Signature.Email, rebasedCommits[i].Author.Email);
                    Assert.Equal(Constants.Signature2.Name, rebasedCommits[i].Committer.Name);
                    Assert.Equal(Constants.Signature2.Email, rebasedCommits[i].Committer.Email);
                }
            }
        }

        [Fact]
        public void CanContinueRebase()
        {
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();
            var path = Repository.Init(scd.DirectoryPath);
            using (Repository repo = new Repository(path))
            {
                ConstructRebaseTestRepository(repo);

                repo.Checkout(topicBranch1Name);
                Assert.False(repo.RetrieveStatus().IsDirty);

                Branch branch = repo.Branches[topicBranch1Name];
                Branch upstream = repo.Branches[conflictBranch1Name];
                Branch onto = repo.Branches[conflictBranch1Name];

                int beforeStepCallCount = 0;
                int afterStepCallCount = 0;
                bool wasCheckoutProgressCalled = false;
                bool wasCheckoutNotifyCalled = false;

                RebaseOptions options = new RebaseOptions()
                {
                    RebaseStepStarting = x => beforeStepCallCount++,
                    RebaseStepCompleted = x => afterStepCallCount++,
                    OnCheckoutProgress = (x, y, z) => wasCheckoutProgressCalled = true,
                    OnCheckoutNotify = (x, y) => { wasCheckoutNotifyCalled = true; return true; },
                    CheckoutNotifyFlags = CheckoutNotifyFlags.Updated | CheckoutNotifyFlags.Conflict,
                };

                RebaseResult rebaseResult = repo.Rebase.Start(branch, upstream, onto, Constants.Signature, options);

                // Verify that we have a conflict.
                Assert.Equal(CurrentOperation.RebaseMerge, repo.Info.CurrentOperation);
                Assert.Equal(RebaseStatus.Conflicts, rebaseResult.Status);
                Assert.True(repo.RetrieveStatus().IsDirty);
                Assert.False(repo.Index.IsFullyMerged);
                Assert.Equal(0, rebaseResult.CompletedStepCount);
                Assert.Equal(3, rebaseResult.TotalStepCount);

                // Verify that expected callbacks were called
                Assert.Equal(1, beforeStepCallCount);
                Assert.Equal(0, afterStepCallCount);
                Assert.True(wasCheckoutProgressCalled, "CheckoutProgress callback was not called.");

                // TODO: investigate following statement.
                // Assert.True(wasCheckoutNotifyCalled, "CheckoutNotify callback was not called.");

                // Resolve the conflict
                foreach (Conflict conflict in repo.Index.Conflicts)
                {
                    Touch(repo.Info.WorkingDirectory,
                          conflict.Theirs.Path,
                          repo.Lookup<Blob>(conflict.Theirs.Id).GetContentText(new FilteringOptions(conflict.Theirs.Path)));
                    repo.Stage(conflict.Theirs.Path);
                }

                Assert.True(repo.Index.IsFullyMerged);

                // Clear the flags:
                wasCheckoutProgressCalled = false; wasCheckoutNotifyCalled = false;
                RebaseResult continuedRebaseResult = repo.Rebase.Continue(Constants.Signature, options);

                Assert.NotNull(continuedRebaseResult);
                Assert.Equal(RebaseStatus.Complete, continuedRebaseResult.Status);
                Assert.False(repo.RetrieveStatus().IsDirty);
                Assert.True(repo.Index.IsFullyMerged);
                Assert.Equal(0, rebaseResult.CompletedStepCount);
                Assert.Equal(3, rebaseResult.TotalStepCount);

                Assert.Equal(3, beforeStepCallCount);
                Assert.Equal(3, afterStepCallCount);
                Assert.True(wasCheckoutProgressCalled, "CheckoutProgress callback was not called.");
                Assert.True(wasCheckoutNotifyCalled, "CheckoutNotify callback was not called.");
            }
        }

        [Fact]
        public void CanQueryRebaseOperation()
        {
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();
            var path = Repository.Init(scd.DirectoryPath);
            using (Repository repo = new Repository(path))
            {
                ConstructRebaseTestRepository(repo);

                repo.Checkout(topicBranch1Name);
                Assert.False(repo.RetrieveStatus().IsDirty);

                Branch branch = repo.Branches[topicBranch1Name];
                Branch upstream = repo.Branches[conflictBranch1Name];
                Branch onto = repo.Branches[conflictBranch1Name];

                RebaseResult rebaseResult = repo.Rebase.Start(branch, upstream, onto, Constants.Signature, null);

                // Verify that we have a conflict.
                Assert.Equal(RebaseStatus.Conflicts, rebaseResult.Status);
                Assert.True(repo.RetrieveStatus().IsDirty);
                Assert.False(repo.Index.IsFullyMerged);
                Assert.Equal(0, rebaseResult.CompletedStepCount);
                Assert.Equal(3, rebaseResult.TotalStepCount);

                RebaseStepInfo info = repo.Rebase.GetCurrentStepInfo();
                Assert.Equal(0, info.CurrentStep);
                Assert.Equal(3, info.TotalStepCount);
                Assert.Equal(RebaseStepOperation.Pick, info.Type);
            }
        }

        [Fact]
        public void CanAbortRebase()
        {
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();
            var path = Repository.Init(scd.DirectoryPath);
            using (Repository repo = new Repository(path))
            {
                ConstructRebaseTestRepository(repo);

                repo.Checkout(topicBranch1Name);
                Assert.False(repo.RetrieveStatus().IsDirty);

                Branch branch = repo.Branches[topicBranch1Name];
                Branch upstream = repo.Branches[conflictBranch1Name];
                Branch onto = repo.Branches[conflictBranch1Name];

                RebaseResult rebaseResult = repo.Rebase.Start(branch, upstream, onto, Constants.Signature, null);

                // Verify that we have a conflict.
                Assert.Equal(RebaseStatus.Conflicts, rebaseResult.Status);
                Assert.True(repo.RetrieveStatus().IsDirty);
                Assert.False(repo.Index.IsFullyMerged);
                Assert.Equal(0, rebaseResult.CompletedStepCount);
                Assert.Equal(3, rebaseResult.TotalStepCount);

                repo.Rebase.Abort(Constants.Signature);
                Assert.False(repo.RetrieveStatus().IsDirty);
                Assert.True(repo.Index.IsFullyMerged);
                Assert.Equal(CurrentOperation.None, repo.Info.CurrentOperation);
            }
        }

        [Fact]
        public void RebaseWhileAlreadyRebasingThrows()
        {
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();
            var path = Repository.Init(scd.DirectoryPath);
            using (Repository repo = new Repository(path))
            {
                ConstructRebaseTestRepository(repo);

                repo.Checkout(topicBranch1Name);
                Assert.False(repo.RetrieveStatus().IsDirty);

                Branch branch = repo.Branches[topicBranch1Name];
                Branch upstream = repo.Branches[conflictBranch1Name];
                Branch onto = repo.Branches[conflictBranch1Name];

                RebaseResult rebaseResult = repo.Rebase.Start(branch, upstream, onto, Constants.Signature, null);

                // Verify that we have a conflict.
                Assert.Equal(RebaseStatus.Conflicts, rebaseResult.Status);
                Assert.True(repo.RetrieveStatus().IsDirty);
                Assert.Equal(CurrentOperation.RebaseMerge, repo.Info.CurrentOperation);

                Assert.Throws<LibGit2SharpException>(() =>
                    repo.Rebase.Start(branch, upstream, onto, Constants.Signature, null));
                }
        }

        [Fact]
        public void RebaseOperationsWithoutRebasingThrow()
        {
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();
            var path = Repository.Init(scd.DirectoryPath);
            using (Repository repo = new Repository(path))
            {
                ConstructRebaseTestRepository(repo);

                repo.Checkout(topicBranch1Name);

                Assert.Throws<NotFoundException>(() =>
                    repo.Rebase.Continue(Constants.Signature, new RebaseOptions()));

                Assert.Throws<NotFoundException>(() =>
                    repo.Rebase.Abort(Constants.Signature));
            }
        }

        [Fact]
        public void CurrentStepInfoIsNullWhenNotRebasing()
        {
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();
            var path = Repository.Init(scd.DirectoryPath);
            using (Repository repo = new Repository(path))
            {
                ConstructRebaseTestRepository(repo);
                repo.Checkout(topicBranch1Name);

                Assert.Null(repo.Rebase.GetCurrentStepInfo());
            }
        }

        private void ConstructRebaseTestRepository(Repository repo)
        {
            // Constructs a graph that looks like:
            //                         * -- * -- *   (modifications to c.txt)
            //                        /          |
            //                       /           T2
            //                      /
            //           * -- * -- *                 (modifications to b.txt)
            //          /          |
            //         /           T1
            //        /
            // *--*--*--*--*--*----
            //       |        |    \
            //       M1       M2    \
            //                       ---*
            //                          |
            //                          C1
            const string lineEnding = "\r\n";

            string filePathA = "a.txt";
            const string fileContentA1 = "A1";

            string filePathB = "b.txt";
            const string fileContentB1 = "B1";
            const string fileContentB2 = "B2";
            const string fileContentB3 = "B3";
            const string fileContentB4 = "B4";

            string filePathC = "c.txt";
            const string fileContentC1 = "C1";
            const string fileContentC2 = "C2";
            const string fileContentC3 = "C3";
            const string fileContentC4 = "C4";

            string filePathD = "d.txt";
            const string fileContentD1 = "D1";
            const string fileContentD2 = "D2";
            const string fileContentD3 = "D3";

            string workdir = repo.Info.WorkingDirectory;
            Commit commit = null;

            Touch(workdir, filePathA, fileContentA1);
            repo.Stage(filePathA);
            commit = repo.Commit("commit 1", Constants.Signature, Constants.Signature, new CommitOptions());

            Touch(workdir, filePathB, fileContentB1);
            repo.Stage(filePathB);
            commit = repo.Commit("commit 2", Constants.Signature, Constants.Signature, new CommitOptions());

            Touch(workdir, filePathC, fileContentC1);
            repo.Stage(filePathC);
            commit = repo.Commit("commit 3", Constants.Signature, Constants.Signature, new CommitOptions());

            repo.CreateBranch(masterBranch1Name, commit, Constants.Signature);

            Touch(workdir, filePathB, string.Join(lineEnding, fileContentB1, fileContentB2));
            repo.Stage(filePathB);
            commit = repo.Commit("commit 4", Constants.Signature, Constants.Signature, new CommitOptions());

            Touch(workdir, filePathB, string.Join(lineEnding, fileContentB1, fileContentB2, fileContentB3));
            repo.Stage(filePathB);
            commit = repo.Commit("commit 5", Constants.Signature, Constants.Signature, new CommitOptions());

            Touch(workdir, filePathB, string.Join(lineEnding, fileContentB1, fileContentB2, fileContentB3, fileContentB4));
            repo.Stage(filePathB);
            commit = repo.Commit("commit 6", Constants.Signature, Constants.Signature, new CommitOptions());

            repo.CreateBranch(topicBranch1Name, commit, Constants.Signature);

            Touch(workdir, filePathC, string.Join(lineEnding, fileContentC1, fileContentC2));
            repo.Stage(filePathC);
            commit = repo.Commit("commit 7", Constants.Signature, Constants.Signature, new CommitOptions());

            Touch(workdir, filePathC, string.Join(lineEnding, fileContentC1, fileContentC2, fileContentC3));
            repo.Stage(filePathC);
            commit = repo.Commit("commit 8", Constants.Signature, Constants.Signature, new CommitOptions());

            Touch(workdir, filePathC, string.Join(lineEnding, fileContentC1, fileContentC2, fileContentC3, fileContentC4));
            repo.Stage(filePathC);
            commit = repo.Commit("commit 9", Constants.Signature, Constants.Signature, new CommitOptions());

            repo.CreateBranch(topicBranch2Name, commit, Constants.Signature);

            repo.Checkout(masterBranch1Name);
            Touch(workdir, filePathD, fileContentD1);
            repo.Stage(filePathD);
            commit = repo.Commit("commit 10", Constants.Signature, Constants.Signature, new CommitOptions());

            Touch(workdir, filePathD, string.Join(lineEnding, fileContentD1, fileContentD2));
            repo.Stage(filePathD);
            commit = repo.Commit("commit 11", Constants.Signature, Constants.Signature, new CommitOptions());

            Touch(workdir, filePathD, string.Join(lineEnding, fileContentD1, fileContentD2, fileContentD3));
            repo.Stage(filePathD);
            commit = repo.Commit("commit 12", Constants.Signature, Constants.Signature, new CommitOptions());

            repo.CreateBranch(masterBranch2Name, commit, Constants.Signature);

            // Create commit / branch that conflicts with T1 and T2
            Touch(workdir, filePathB, string.Join(lineEnding, fileContentB1, fileContentB2 + fileContentB3 + fileContentB4));
            repo.Stage(filePathB);
            commit = repo.Commit("commit 13", Constants.Signature, Constants.Signature, new CommitOptions());
            repo.CreateBranch(conflictBranch1Name, commit, Constants.Signature);
        }
    }
}
