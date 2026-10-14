# 香檳塔贈送與分潤

2026-10-14；本機實作，尚未發布或套用任何環境的 migration。

原有事件分類只支援訂單快照及通知辨識。本次依使用者確認，香檳塔不可使用餐點信物折抵，每筆商品可送給店家或指定有效店員；指定店員沿用該營業日結算規則的指名服務比例，額外加給薪資。一般日的店家餘額進入共同營收；活動日先取出指定店員分潤，再分配剩餘活動池。套餐繼承香檳塔分類時，以整組價格適用相同規則。

完整規則、歷史資料邊界、API／Web 契約、驗證限制及發布順序見 [Web 香檳塔盤點](../../ToBeClarify-web/docs/champagne-tower-20261014.md)。

發布前先備份資料庫並套用 [20261014_01_champagne_recipients.sql](../db/migrations/20261014_01_champagne_recipients.sql)。這支 migration 新增訂單品項的分類、受贈 ID 與姓名快照，不會依當前菜單或指名資料回填歷史。API 必須在 migration 完成後才部署；舊 Web 未提供贈送欄位時，新訂單預設送給店家，API 仍會排除香檳塔的信物折抵。

結算以獨立 `champagne_recipient` 列額外支付；已完成結算繼續讀取原結果快照。沒有新增自動實收／實退紀錄，也沒有提交後修改受贈者入口。

[金額回歸檢查](../tests/ChampagneAccounting/Program.cs)使用記憶體來源，手動執行 `dotnet run --project tests/ChampagneAccounting/ChampagneAccounting.csproj --configuration Release`。本次 22 個金額斷言通過，涵蓋共同池、額外薪資、混單折抵、分人歸屬、取消後來源、折讓、活動分配、實收縮放及舊活動帳務相容性；SQL migration、持久化與實際完整操作仍待整合驗收。此專案沒有加入 `dev` 部署流程。

2026-10-14 發布準備：使用者已授權正式 API 發布；MariaDB migration 已套用，三欄均存在，訂單品項套用前後均為 111 筆且沒有歷史回填。套用前保存結構及筆數快照，未匯出訂單內容。本次發布只做 build／publish、靜態檢查及部署健康檢查，不重跑自動化測試。API 專案的 Compile、Content 與 None 項目均排除 `tests/**`，避免把檢查專案內容帶入正式產物。
