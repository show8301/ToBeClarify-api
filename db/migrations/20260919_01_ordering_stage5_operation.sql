-- Stage 5: customer delivery location and assisted ordering support.
-- Additive and rerunnable; existing order notes and fulfillment history remain intact.
ALTER TABLE `ORDERS`
    ADD COLUMN IF NOT EXISTS `CUSTOMER_LOCATION` VARCHAR(200) NULL AFTER `CUSTOMER_NOTE`;
