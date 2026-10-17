# 工作台偏好、計薪職位與支援工作

2026-10-17。本地實作，未執行 migration 或部署。此階段不修改結算引擎。

## 身份與偏好

登入及 `GET /api/admin/auth/me` 新增 `preferredWorkbench`（nullable）及 `availableWorkbenchRoles`。clerk 固定為 service／designated，manager／developer 為 service／designated／manager。

`PUT /api/admin/auth/me/preferences` 使用現有 Admin 登入驗證：

```json
{ "preferredWorkbench": "designated" }
```

成功回傳完整 `AdminIdentityDto`。目標帳號取自登入者，不接受客戶端指定帳號；越權偏好回傳 `WORKBENCH_NOT_ALLOWED`。保存時於 SQL 再檢查目前有效帳號角色，避免角色變更期間寫入經理偏好。新偏好不改變 JWT 角色或店員設定。

## 班表計薪職位

`POST/PUT /api/admin/duty-plans[/{id}]` 增加選填 `payrollRole`，回應亦包含此欄位。有效值為 service／designated／manager／backstage；同一班只有一個值。一般店員只能提報本人班表，經理核准後生效；經理自行儲存則直接核准。選 manager 時，目標店員必須綁定有效 manager／developer 帳號。

舊呼叫端省略欄位時保留既有設定；新紀錄僅從無歧義舊角色推導。上班班表無法確認唯一職位時回傳 `DUTY_PLAN_PAYROLL_ROLE_REQUIRED`。編輯既有班表須核對 ID、店員與日期，不允許將一筆既有 ID 改作其他店員／日期的班表。

店員回應增加 `canUseManagerPayrollRole`；`todayWorkMode` 增加 `payrollRole` 及 `allowedSupportRoles`。計薪職位由核准班表讀取，不能透過每日工作狀態 API 改寫。

**此欄位尚未接入結算。** 原結算服務仍讀取舊角色與結算輸入，費率、歷史結算、幕後分潤、指名收入與小費計算未變更；後續需獨立完成結算來源與收入組成調整。

## 目前支援工作與接單

`PUT /api/admin/staff-members/{id}/today-work-mode` 支援以下單項調整：

```json
{ "businessDate": "2026-10-17", "supportRole": "service", "supportEnabled": true }
```

或只提供 `businessDate` 與 `stopAcceptingNewOrders`。`supportRole` 與 `supportEnabled` 須同時提供，不可與舊版完整 `activeRoles` 同時使用。省略的狀態保留最新資料，不能從讀取前的舊狀態覆寫。

API 在同一交易內鎖定店員、班表與每日狀態：變更支援須在核准班次內；service／backstage 可支援，designated 須長期 `isNominatable`。不再要求 `activeRoles` 是 `scheduledRoles` 的子集。原 `activeRoles` 完整清單維持相容；`scheduledRoles` 與 `isWorking` 只保留相容輸入，不能修改核准班表或計薪欄位。

主要錯誤：`STAFF_WORK_MODE_DATE_CHANGED`、`STAFF_SUPPORT_CHANGE_INVALID`、`STAFF_SUPPORT_REQUIRES_DUTY`、`STAFF_SUPPORT_NOT_ALLOWED`。

上班班表的儲存／核准保留既有目前支援設定；休假清空支援。依職位廣播仍使用當下核准執勤與 `activeRoles`，不使用 `payrollRole` 或帳號偏好。本人指名通知的訂單歸屬規則不變。

## Migration 與驗證

先執行 [20261017_01_workbench_preferences_payroll_role.sql](../db/migrations/20261017_01_workbench_preferences_payroll_role.sql)，再發布 API；新增 nullable 欄位，不刪除舊角色或變更歷史結算。單一前台角色加幕後參與只推導前台計薪角色；多個前台職位保留 null，交由經理確認。

本地 .NET 10 Release build 通過；未執行自動化測試套件、資料庫 migration 或登入後實際交易驗證。API 仍由 `main` 部署至唯一正式主機，`dev` 僅建置。
