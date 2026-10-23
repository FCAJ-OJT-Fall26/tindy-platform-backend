namespace Tindy.Application.Models.Storage;

// Created by the storage adapter, never accepted from an HTTP request.
// Persist before writing so interrupted uploads remain discoverable by cleanup.
public sealed record StorageWriteTarget(string StorageProvider, string StorageContainer, string StorageKey);
