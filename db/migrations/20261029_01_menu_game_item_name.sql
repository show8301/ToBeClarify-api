-- Optional in-game delivery name; existing menu items remain unchanged.
ALTER TABLE `MENU_ITEMS`
    ADD COLUMN IF NOT EXISTS `GAME_ITEM_NAME` VARCHAR(100) NULL AFTER `ITEM_NAME`;
