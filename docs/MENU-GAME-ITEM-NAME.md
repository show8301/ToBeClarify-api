# 送餐遊戲內品項名稱

`MENU_ITEMS.GAME_ITEM_NAME` 為選填的遊戲內物品名稱，最多 100 字；空值表示尚未設定，不推測物品名稱。後台菜單 API 使用 `gameItemName`。舊版呼叫未提供欄位時保留既有值；新版以空字串清除。

分項履約回應的 `deliveryItems` 提供送餐提示。單點以原訂單品項識別碼查詢；套餐使用訂單菜單快照中的子品項及每份數量，不以目前套餐組成取代。名稱取目前菜單設定，可能隨設定更動；已刪除品項或未設定時顯示未設定。缺少可解析快照的舊套餐不猜測其組成。此資料僅為送餐提示，不影響報價、訂單金額、數量或履約狀態。

發布時先依 DBA runbook 核對並套用 `20261029_01_menu_game_item_name.sql`，再發布 API，最後發布 Web dev。資料庫備份、執行結果與 API／Web 實際驗證另行記錄；migration 不回填或改寫既有菜單。

2026-10-29 已套用 migration，SHA-256 `E1F11284A1C44B89CE9A7D595E782219762609503EA2FE66D2DF6209B1B9C243`。執行前核對 MariaDB 目標及 ALTER／SELECT 權限，表結構保存於本機忽略路徑 `.dotnet-home-local/migration-evidence/20261029-menu-game-item/MENU_ITEMS.before.sql`。執行後確認欄位為可空 `VARCHAR(100)`；品項維持 16 筆、價格總和 955000，新增欄位均為空值。此紀錄不代表送餐操作已驗收。
