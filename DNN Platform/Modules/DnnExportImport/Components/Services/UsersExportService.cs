﻿#region Copyright
//
// DotNetNuke® - http://www.dnnsoftware.com
// Copyright (c) 2002-2017
// by DotNetNuke Corporation
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and
// to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions
// of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dnn.ExportImport.Components.Common;
using Dnn.ExportImport.Components.Dto;
using Dnn.ExportImport.Components.Dto.Users;
using DotNetNuke.Common.Utilities;
using Dnn.ExportImport.Components.Entities;
using Dnn.ExportImport.Dto.Users;
using DotNetNuke.Data.PetaPoco;
using DotNetNuke.Entities.Portals;
using DotNetNuke.Entities.Users;
using Newtonsoft.Json;
using DataProvider = Dnn.ExportImport.Components.Providers.DataProvider;

namespace Dnn.ExportImport.Components.Services
{
    /// <summary>
    /// Service to export/import users.
    /// </summary>
    public class UsersExportService : BasePortableService
    {
        public override string Category => Constants.Category_Users;

        public override string ParentCategory => null;

        public override uint Priority => 0;

        public override void ExportData(ExportImportJob exportJob, ExportDto exportDto)
        {
            var fromDate = exportDto.FromDate?.DateTime;
            var toDate = exportDto.ToDate;
            if (CheckCancelled(exportJob)) return;

            var portalId = exportJob.PortalId;
            var pageIndex = 0;
            const int pageSize = Constants.DefaultPageSize;
            var totalUsersExported = 0;
            var totalUserRolesExported = 0;
            var totalPortalsExported = 0;
            var totalProfilesExported = 0;
            var totalAuthenticationExported = 0;
            var totalAspnetUserExported = 0;
            var totalAspnetMembershipExported = 0;
            var isDirty = false;
            var dataReader = DataProvider.Instance()
                .GetAllUsers(portalId, pageIndex, pageSize, exportDto.IncludeDeletions, toDate,
                    fromDate);
            var allUsers = CBO.FillCollection<ExportUser>(dataReader).ToList();
            var firstOrDefault = allUsers.FirstOrDefault();
            if (firstOrDefault == null) return;

            var totalUsers = allUsers.Any() ? firstOrDefault.Total : 0;
            var totalPages = Util.CalculateTotalPages(totalUsers, pageSize);

            //Skip the export if all the users has been processed already.
            if (CheckPoint.Stage >= totalPages)
                return;

            //Check if there is any pending stage or partially processed data.
            if (CheckPoint.Stage > 0)
            {
                isDirty = true;
                pageIndex = CheckPoint.Stage;
                if (pageIndex > 0)
                {
                    dataReader = DataProvider.Instance()
                        .GetAllUsers(portalId, pageIndex, pageSize, false, toDate, fromDate);
                    allUsers =
                        CBO.FillCollection<ExportUser>(dataReader).ToList();
                }
            }

            var totalUsersToBeProcessed = totalUsers - pageIndex * pageSize;

            //Update the total items count in the check points. This should be updated only once.
            CheckPoint.TotalItems = CheckPoint.TotalItems <= 0 ? totalUsers : CheckPoint.TotalItems;
            CheckPoint.ProcessedItems = CheckPoint.Stage * pageSize;
            if (CheckPointStageCallback(this)) return;

            var progressStep = totalUsers > 100 ? totalUsers / 100 : 1;
            try
            {
                do
                {
                    if (CheckCancelled(exportJob)) return;

                    //Delete data from Local DB if process was some how broken/stopped in between.
                    if (!isDirty)
                    {
                        //Add 1000 users to Lite DB.
                        Repository.CreateItems(allUsers, null);
                    }
                    isDirty = false;
                    var exportAspnetUserList = new List<ExportAspnetUser>();
                    var exportAspnetMembershipList = new List<ExportAspnetMembership>();
                    var exportUserRoleList = new List<ExportUserRole>();
                    var exportUserPortalList = new List<ExportUserPortal>();
                    var exportUserAuthenticationList = new List<ExportUserAuthentication>();
                    var exportUserProfileList = new List<ExportUserProfile>();

                    foreach (var user in allUsers)
                    {
                        if (CheckCancelled(exportJob)) return;

                        var aspnetUser =
                            CBO.FillObject<ExportAspnetUser>(DataProvider.Instance()
                                .GetAspNetUser(user.Username, Util.ConvertToDbUtcTime(toDate).Value,
                                    Util.ConvertToDbUtcTime(fromDate)));
                        if (aspnetUser != null)
                        {
                            aspnetUser.ReferenceId = user.Id;
                            exportAspnetUserList.Add(aspnetUser);
                            var aspnetMembership =
                                    CBO.FillObject<ExportAspnetMembership>(
                                        DataProvider.Instance()
                                            .GetUserMembership(aspnetUser.UserId, aspnetUser.ApplicationId));
                            if (aspnetMembership != null)
                            {
                                aspnetMembership.ReferenceId = user.Id;
                                exportAspnetMembershipList.Add(aspnetMembership);
                            }
                        }

                        var userRoles =
                            CBO.FillCollection<ExportUserRole>(DataProvider.Instance()
                                .GetUserRoles(portalId, user.UserId, toDate, fromDate));
                        userRoles.ForEach(x => { x.ReferenceId = user.Id; });
                        exportUserRoleList.AddRange(userRoles);

                        var userPortal =
                            CBO.FillObject<ExportUserPortal>(DataProvider.Instance()
                                .GetUserPortal(portalId, user.UserId, toDate, fromDate));
                        if (userPortal != null)
                        {
                            userPortal.ReferenceId = user.Id;
                            exportUserPortalList.Add(userPortal);
                        }

                        var userAuthentication =
                            CBO.FillObject<ExportUserAuthentication>(
                                DataProvider.Instance().GetUserAuthentication(user.UserId, toDate, fromDate));
                        if (userAuthentication != null)
                        {
                            userAuthentication.ReferenceId = user.Id;
                            exportUserAuthenticationList.Add(userAuthentication);
                        }

                        var userProfiles =
                            CBO.FillCollection<ExportUserProfile>(DataProvider.Instance()
                                .GetUserProfile(portalId, user.UserId, toDate, fromDate));
                        userProfiles.ForEach(x => { x.ReferenceId = user.Id; });
                        exportUserProfileList.AddRange(userProfiles);

                        CheckPoint.ProcessedItems++;

                        if (CheckPoint.ProcessedItems % progressStep == 0)
                            CheckPoint.Progress += 1;
                        if (CheckPoint.ProcessedItems % 100 == 0 && CheckPointStageCallback(this)) return;
                    }

                    Repository.CreateItems(exportAspnetUserList, null);
                    totalAspnetUserExported += exportAspnetUserList.Count;

                    Repository.CreateItems(exportAspnetMembershipList, null);
                    totalAspnetMembershipExported += exportAspnetMembershipList.Count;

                    Repository.CreateItems(exportUserPortalList, null);
                    totalPortalsExported += exportUserPortalList.Count;

                    Repository.CreateItems(exportUserProfileList, null);
                    totalProfilesExported += exportUserProfileList.Count;

                    Repository.CreateItems(exportUserRoleList, null);
                    totalUserRolesExported += exportUserRoleList.Count;

                    Repository.CreateItems(exportUserAuthenticationList, null);
                    totalAuthenticationExported += exportUserAuthenticationList.Count;

                    totalUsersExported += allUsers.Count;
                    CheckPoint.Stage++;
                    if (CheckPointStageCallback(this)) return;

                    pageIndex++;
                    dataReader = DataProvider.Instance()
                        .GetAllUsers(portalId, pageIndex, pageSize, false, toDate, fromDate);
                    allUsers =
                        CBO.FillCollection<ExportUser>(dataReader).ToList();
                } while (totalUsersExported < totalUsersToBeProcessed);
                CheckPoint.Progress = 100;
            }
            finally
            {
                CheckPointStageCallback(this);
                Result.AddSummary("Exported Users", totalUsersExported.ToString());
                Result.AddSummary("Exported User Portals", totalPortalsExported.ToString());
                Result.AddSummary("Exported User Roles", totalUserRolesExported.ToString());
                Result.AddSummary("Exported User Profiles", totalProfilesExported.ToString());
                Result.AddSummary("Exported User Authentication", totalAuthenticationExported.ToString());
                Result.AddSummary("Exported Aspnet User", totalAspnetUserExported.ToString());
                Result.AddSummary("Exported Aspnet Membership", totalAspnetMembershipExported.ToString());
            }
        }

        public override void ImportData(ExportImportJob importJob, ImportDto importDto)
        {
            if (CheckCancelled(importJob)) return;

            var pageIndex = 0;
            const int pageSize = Constants.DefaultPageSize;
            var totalUsersImported = 0;
            var totalPortalsImported = 0;
            var totalAspnetUserImported = 0;
            var totalAspnetMembershipImported = 0;

            var totalUsers = Repository.GetCount<ExportUser>();
            var totalPages = Util.CalculateTotalPages(totalUsers, pageSize);

            var skip = GetCurrentSkip();
            var currentIndex = skip;
            //Skip the import if all the users has been processed already.
            if (CheckPoint.Stage >= totalPages && skip == 0)
                return;

            pageIndex = CheckPoint.Stage;

            var totalUsersToBeProcessed = totalUsers - pageIndex * pageSize - skip;
            //Update the total items count in the check points. This should be updated only once.
            CheckPoint.TotalItems = CheckPoint.TotalItems <= 0 ? totalUsers : CheckPoint.TotalItems;
            if (CheckPointStageCallback(this)) return;

            var progressStep = totalUsers > 100 ? totalUsers / 100 : 1;
            try
            {
                while (totalUsersImported < totalUsersToBeProcessed)
                {
                    if (CheckCancelled(importJob)) return;
                    var users =
                        Repository.GetAllItems<ExportUser>(null, true, pageIndex * pageSize + skip, pageSize).ToList();
                    skip = 0;
                    foreach (var user in users)
                    {
                        if (CheckCancelled(importJob)) return;

                        var aspNetUser = Repository.GetRelatedItems<ExportAspnetUser>(user.Id).FirstOrDefault();
                        var aspnetMembership =
                            Repository.GetRelatedItems<ExportAspnetMembership>(user.Id).FirstOrDefault();

                        var userPortal = Repository.GetRelatedItems<ExportUserPortal>(user.Id).FirstOrDefault();
                        ProcessUser(importJob, importDto, user, userPortal, aspNetUser != null ? new ImportAspnetUser(aspNetUser) : null,
                           aspnetMembership != null ? new ImportAspnetMembership(aspnetMembership) : null);
                        totalAspnetUserImported += aspNetUser != null ? 1 : 0;
                        totalAspnetMembershipImported += aspnetMembership != null ? 1 : 0;
                        ProcessUserPortal(importJob, importDto, userPortal, user.UserId);
                        totalPortalsImported += userPortal != null ? 1 : 0;

                        currentIndex++;
                        CheckPoint.ProcessedItems++;
                        totalUsersImported++;
                        if (CheckPoint.ProcessedItems % progressStep == 0)
                            CheckPoint.Progress += 1;

                        CheckPoint.StageData = null;
                        //After every 100 items, call the checkpoint stage. This is to avoid too many frequent updates to DB.
                        if (currentIndex % 100 == 0 && CheckPointStageCallback(this)) return;
                    }
                    currentIndex = 0;//Reset current index to 0
                    pageIndex++;
                    CheckPoint.Stage++;
                    CheckPoint.StageData = null;
                    if (CheckPointStageCallback(this)) return;
                }
                CheckPoint.Progress = 100;
            }
            finally
            {
                CheckPoint.StageData = currentIndex > 0 ? JsonConvert.SerializeObject(new { skip = currentIndex }) : null;
                CheckPointStageCallback(this);
                Result.AddSummary("Imported Users", totalUsersImported.ToString());
                Result.AddSummary("Imported User Portals", totalPortalsImported.ToString());
                Result.AddSummary("Imported Aspnet Users", totalAspnetUserImported.ToString());
                Result.AddSummary("Imported Aspnet Memberships", totalAspnetMembershipImported.ToString());
            }
        }

        public override int GetImportTotal()
        {
            return Repository.GetCount<ExportUser>();
        }

        private void ProcessUser(ExportImportJob importJob, ImportDto importDto, ExportUser user,
            ExportUserPortal userPortal, ImportAspnetUser aspnetUser, ImportAspnetMembership aspnetMembership)
        {
            if (user == null) return;
            var existingUser = CBO.FillObject<ExportUser>(DotNetNuke.Data.DataProvider.Instance().GetUserByUsername(importJob.PortalId, user.Username));
            var isUpdate = false;
            if (existingUser != null)
            {
                switch (importDto.CollisionResolution)
                {
                    case CollisionResolution.Overwrite:
                        isUpdate = true;
                        break;
                    case CollisionResolution.Ignore:
                        Result.AddLogEntry("Ignored user", user.Username);
                        return;
                    default:
                        throw new ArgumentOutOfRangeException(importDto.CollisionResolution.ToString());
                }
            }
            user.FirstName = string.IsNullOrEmpty(user.FirstName) ? string.Empty : user.FirstName;
            user.LastName = string.IsNullOrEmpty(user.LastName) ? string.Empty : user.LastName;

            if (isUpdate)
            {
                var vanityUrl = userPortal != null ? userPortal.VanityUrl : UserController.Instance.GetUser(importJob.PortalId, existingUser.UserId)?.VanityUrl;
                if (string.IsNullOrEmpty(vanityUrl))
                {

                }
                var modifiedBy = Util.GetUserIdByName(importJob, user.LastModifiedByUserId, user.LastModifiedByUserName);
                user.UserId = existingUser.UserId;
                DotNetNuke.Data.DataProvider.Instance().UpdateUser(user.UserId, importJob.PortalId, user.FirstName, user.LastName, user.IsSuperUser,
                        user.Email, user.DisplayName, vanityUrl, user.UpdatePassword, aspnetMembership?.IsApproved ?? true,
                        false, user.LastIpAddress, user.PasswordResetToken ?? Guid.NewGuid(), user.PasswordResetExpiration ?? DateTime.Now.AddYears(10), user.IsDeleted, modifiedBy);

                ProcessUserMembership(aspnetUser, aspnetMembership, true);
            }
            else
            {
                var createdBy = Util.GetUserIdByName(importJob, user.CreatedByUserId, user.CreatedByUserName);
                user.UserId = DotNetNuke.Data.DataProvider.Instance().AddUser(importJob.PortalId, user.Username, user.FirstName, user.LastName, user.AffiliateId,
                        user.IsSuperUser, user.Email, user.DisplayName, user.UpdatePassword, aspnetMembership?.IsApproved ?? true, createdBy);

                ProcessUserMembership(aspnetUser, aspnetMembership);
            }
        }

        private void ProcessUserPortal(ExportImportJob importJob, ImportDto importDto,
            ExportUserPortal userPortal, int userId)
        {
            if (userPortal == null) return;
            var existingPortal =
                CBO.FillObject<ExportUserPortal>(DataProvider.Instance().GetUserPortal(importJob.PortalId, userId, DateUtils.GetDatabaseUtcTime().AddYears(1), null));
            var isUpdate = false;
            if (existingPortal != null)
            {
                switch (importDto.CollisionResolution)
                {
                    case CollisionResolution.Overwrite:
                        isUpdate = true;
                        break;
                    case CollisionResolution.Ignore:
                        //Result.AddLogEntry("Ignored user portal", $"{username}/{userPortal.PortalId}");
                        return;
                    default:
                        throw new ArgumentOutOfRangeException(importDto.CollisionResolution.ToString());
                }
            }

            if (isUpdate)
            {
                //Nothing to do
            }
            else
            {
                DotNetNuke.Data.DataProvider.Instance().AddUserPortal(importJob.PortalId, userId);
            }
        }

        private void ProcessUserMembership(ImportAspnetUser aspNetUser, ImportAspnetMembership aspnetMembership,
            bool update = false)
        {
            if (aspNetUser == null) return;
            using (var db =
                new PetaPocoDataContext(DotNetNuke.Data.DataProvider.Instance().Settings["connectionStringName"],
                    "aspnet_"))
            {
                var repAspnetUsers = db.GetRepository<ImportAspnetUser>();
                var repAspnetMembership = db.GetRepository<ImportAspnetMembership>();
                var updateStep = 0;
                if (update)
                {
                    var existingAspnetUser =
                        CBO.FillObject<ExportAspnetUser>(DataProvider.Instance()
                            .GetAspNetUser(aspNetUser.UserName, DateUtils.GetDatabaseUtcTime().AddYears(1), null));
                    if (existingAspnetUser != null)
                    {
                        var existingAspnetMembership =
                            CBO.FillObject<ExportAspnetMembership>(
                                DataProvider.Instance()
                                    .GetUserMembership(existingAspnetUser.UserId, existingAspnetUser.ApplicationId));

                        aspNetUser.LastActivityDate = existingAspnetUser.LastActivityDate;
                        aspNetUser.UserId = existingAspnetUser.UserId;
                        aspNetUser.ApplicationId = existingAspnetUser.ApplicationId;
                        repAspnetUsers.Update(aspNetUser);
                        updateStep++;
                        if (existingAspnetMembership != null && aspnetMembership != null)
                        {
                            aspnetMembership.UserId = existingAspnetMembership.UserId;
                            aspnetMembership.ApplicationId = existingAspnetMembership.ApplicationId;
                            aspnetMembership.CreateDate = existingAspnetMembership.CreateDate;
                            repAspnetMembership.Update(aspnetMembership);
                            updateStep++;
                        }
                    }
                }

                if (updateStep < 2)
                {
                    var applicationId = db.ExecuteScalar<Guid>(CommandType.Text,
                        "SELECT TOP 1 ApplicationId FROM aspnet_Applications");

                    if (updateStep < 1)
                    {
                        //AspnetUser
                        aspNetUser.UserId = Guid.Empty;
                        aspNetUser.ApplicationId = applicationId;
                        aspNetUser.LastActivityDate = DateUtils.GetDatabaseUtcTime();
                        repAspnetUsers.Insert(aspNetUser);
                    }
                    if (updateStep == 1)
                    {
                        var existingAspnetUser =
                            CBO.FillObject<ExportAspnetUser>(
                                DataProvider.Instance()
                                    .GetAspNetUser(aspNetUser.UserName, DateUtils.GetDatabaseUtcTime().AddYears(1),
                                        null));
                        aspNetUser.UserId = existingAspnetUser.UserId;
                    }
                    if (updateStep < 2 && aspnetMembership != null)
                    {
                        //AspnetMembership
                        aspnetMembership.UserId = aspNetUser.UserId;
                        aspnetMembership.ApplicationId = applicationId;
                        aspnetMembership.CreateDate = DateUtils.GetDatabaseUtcTime();
                        aspnetMembership.LastLoginDate =
                            aspnetMembership.LastPasswordChangedDate =
                                aspnetMembership.LastLockoutDate =
                                    aspnetMembership.FailedPasswordAnswerAttemptWindowStart =
                                        aspnetMembership.FailedPasswordAttemptWindowStart =
                                            new DateTime(1754, 1, 1);
                        repAspnetMembership.Insert(aspnetMembership);
                    }
                }
            }
        }

        private int GetCurrentSkip()
        {
            if (!string.IsNullOrEmpty(CheckPoint.StageData))
            {
                dynamic stageData = JsonConvert.DeserializeObject(CheckPoint.StageData);
                return Convert.ToInt32(stageData.skip) ?? 0;
            }
            return 0;
        }
    }
}