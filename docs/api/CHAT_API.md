# Chat contract
Authenticated participants only. Inbox returns first_unread_at and first_unread_message_id; use the ID to place exactly one unread divider even for equal timestamps. REST uses snake_case; SignalR uses camelCase.
GET /api/chat/conversations/{id}/messages accepts limit, before (UTC), before_message_id. Pass both cursor fields returned as next_before/next_before_message_id. Results are chronological.
POST /api/chat/conversations/{id}/read requires {last_read_message_id: UUID}. Watermark advances monotonically to that message, not wall-clock time.
POST /api/chat/conversations/{id}/messages and hub SendMessage share persistence and message.created delivery to all connections of both participants. Reuse client_message_id on retries; deduplicate responses/events by messageId. Delivery is best effort; resync on reconnect.
Hub connections join their user group automatically; conversation.read requests inbox refresh. MarkRead takes conversationId and lastReadMessageId.
Direct conversation creation is serialized per pair and protected by a unique active direct_key. Migration backfill fails on existing duplicate active pairs; review/consolidate histories before migration, never silently delete them.
