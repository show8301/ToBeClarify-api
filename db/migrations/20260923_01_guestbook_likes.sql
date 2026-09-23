-- Anonymous guestbook likes. The visitor key is an HMAC derived by the API;
-- the browser's raw random identifier is never stored in this table.
CREATE TABLE IF NOT EXISTS GUESTBOOK_LIKES (
  MESSAGE_ID CHAR(36) NOT NULL,
  VISITOR_KEY CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  CREATED_AT DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (MESSAGE_ID, VISITOR_KEY)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
