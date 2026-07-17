CREATE TABLE IF NOT EXISTS folders (
    id UUID PRIMARY KEY,
    path TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_folders_path UNIQUE (path)
);

INSERT INTO folders (id, path) VALUES (gen_random_uuid(), '/') ON CONFLICT (path) DO NOTHING;

CREATE TABLE IF NOT EXISTS files (
    id UUID PRIMARY KEY,
    filename TEXT NOT NULL,
    content_type TEXT NOT NULL,
    size BIGINT NOT NULL,
    channel_id BIGINT NOT NULL,
    message_id INT NOT NULL,
    access_hash BIGINT NOT NULL,
    file_reference BYTEA NOT NULL,
    document_id BIGINT NOT NULL,
    folder_id UUID NOT NULL REFERENCES folders(id) ON DELETE RESTRICT,
    tags TEXT[] DEFAULT '{}',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_files_folder_id ON files(folder_id);
CREATE INDEX IF NOT EXISTS idx_files_filename ON files(filename);
CREATE INDEX IF NOT EXISTS idx_files_created_at ON files(created_at DESC);
