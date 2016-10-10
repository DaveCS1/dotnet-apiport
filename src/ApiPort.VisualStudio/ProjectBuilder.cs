﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using static Microsoft.VisualStudio.VSConstants;

namespace ApiPortVS
{
    public class ProjectBuilder
    {
        private IVsSolutionBuildManager2 _buildManager;

        public ProjectBuilder(IVsSolutionBuildManager2 buildManager)
        {
            _buildManager = buildManager;
        }

        public Task<bool> BuildAsync(ICollection<Project> projects)
        {
            var projectHierarchy = projects.Select(project => project.GetHierarchy()).ToArray();
            var buildUpdateFlags = Enumerable.Repeat((uint)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_BUILD, projectHierarchy.Length).ToArray();

            // Launches an asynchronous build operation and returns S_OK immediately if the build begins.
            // The result does not indicate completion or success of the build
            var updateErrorCode = _buildManager.StartUpdateSpecificProjectConfigurations(
                (uint)projects.Count,
                projectHierarchy,
                null,
                null,
                buildUpdateFlags,
                null,
                (uint)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_BUILD,
                0);

            var tcs = new TaskCompletionSource<bool>();

            if (updateErrorCode == S_OK)
            {
                var builder = new ProjectAsyncBuilder(_buildManager, tcs);
                _buildManager.AdviseUpdateSolutionEvents(builder, out builder.UpdateSolutionEventsCookie);
            }
            else
            {
                tcs.SetResult(false);
            }

            return tcs.Task;
        }

        private class ProjectAsyncBuilder : IVsUpdateSolutionEvents
        {
            private readonly TaskCompletionSource<bool> _completionSource;
            private readonly IVsSolutionBuildManager _buildManager;

            /// <summary>
            /// A cookie used to track this instance in IVsSolutionBuildManager solution events.
            /// </summary>
            public uint UpdateSolutionEventsCookie;

            public ProjectAsyncBuilder(IVsSolutionBuildManager manager, TaskCompletionSource<bool> completionSource)
            {
                _buildManager = manager;
                _completionSource = completionSource;
            }

            /// <summary>
            /// Called when the active project configuration for a project in the solution has changed.
            /// </summary>
            /// <param name="pIVsHierarchy">Pointer to an IVsHierarchy object.</param>
            /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code.</returns>
            public int OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy) => S_OK;

            /// <summary>
            /// Called before any build actions have begun. This is the last chance to cancel the build before any building begins.
            /// </summary>
            /// <param name="pfCancelUpdate">Pointer to a flag indicating cancel update.</param>
            /// <returns></returns>
            public int UpdateSolution_Begin(ref int pfCancelUpdate) => S_OK;

            /// <summary>
            /// Called when a build is being cancelled.
            /// </summary>
            /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code.</returns>
            public int UpdateSolution_Cancel() => S_OK;

            /// <summary>
            /// Called when entire solution is done building
            /// </summary>
            /// <param name="fSucceeded">true if no update actions failed</param>
            /// <param name="fModified">true if any update actions succeeded</param>
            /// <param name="fCancelCommand">true if update actions were canceled</param>
            /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code.</returns>
            public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
            {
                const int True = 1;

                _buildManager.UnadviseUpdateSolutionEvents(UpdateSolutionEventsCookie);

                if (fCancelCommand == True)
                {
                    _completionSource.SetResult(false);
                }
                else if (fSucceeded == True)
                {
                    _completionSource.SetResult(true);
                }
                else
                {
                    _completionSource.SetResult(false);
                }

                return S_OK;
            }

            /// <summary>
            /// Called before the first project configuration is about to be built.
            /// </summary>
            /// <param name="pfCancelUpdate">Pointer to a flag indicating cancel update.</param>
            /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code.</returns>
            public int UpdateSolution_StartUpdate(ref int pfCancelUpdate) => S_OK;
        }
    }
}