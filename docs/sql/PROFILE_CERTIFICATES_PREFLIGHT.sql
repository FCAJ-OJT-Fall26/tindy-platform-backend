-- Read-only. Run against the intended database BEFORE scheduling the migration.
-- No rows = no duplicate linked attachments, regardless of media status.
BEGIN TRANSACTION READ ONLY;

SELECT
    a.certificate_id,
    count(*) AS attachment_count,
    array_agg(a.attachment_id ORDER BY a.attachment_id) AS attachment_ids,
    array_agg(a.media_asset_id ORDER BY a.attachment_id) AS media_asset_ids,
    jsonb_agg(jsonb_build_object(
        'attachment_id', a.attachment_id,
        'owner_user_id', a.owner_user_id,
        'media_asset_id', a.media_asset_id,
        'status', m.status,
        'scan_status', m.scan_status,
        'visibility', a.visibility
    ) ORDER BY a.attachment_id) AS records_requiring_manual_resolution
FROM user_certificate_attachments a
LEFT JOIN media_assets m ON m.media_asset_id = a.media_asset_id
GROUP BY a.certificate_id
HAVING count(*) > 1
ORDER BY a.certificate_id;

-- These values will be lost when the old columns are dropped. Archive a full
-- database backup before migration; this summary is NOT a backup.
SELECT count(*) AS certificate_count,
    count(credential_id) AS certificates_with_credential_id,
    count(issued_at) AS certificates_with_issued_at,
    count(expires_at) AS certificates_with_expires_at
FROM user_certificates;

COMMIT;
