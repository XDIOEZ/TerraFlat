package com.flatworld.mods;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.Build;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelFileDescriptor;
import android.provider.DocumentsContract;
import android.provider.DocumentsContract.Document;
import android.provider.DocumentsContract.Root;
import android.provider.DocumentsProvider;
import android.util.Log;
import android.webkit.MimeTypeMap;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// 系统文档入口只映射 MOD 文件，不参与游戏内的扫描、启停与重载。
public final class FlatWorldModDocumentsProvider extends DocumentsProvider {
    private static final String ROOT_ID = "mods";
    private static final String TITLE = "FlatWorld MOD";
    private static final String[] ROOT_COLUMNS = { Root.COLUMN_ROOT_ID, Root.COLUMN_DOCUMENT_ID,
        Root.COLUMN_TITLE, Root.COLUMN_FLAGS, Root.COLUMN_MIME_TYPES, Root.COLUMN_ICON };
    private static final String[] DOCUMENT_COLUMNS = { Document.COLUMN_DOCUMENT_ID,
        Document.COLUMN_DISPLAY_NAME, Document.COLUMN_MIME_TYPE, Document.COLUMN_FLAGS,
        Document.COLUMN_SIZE, Document.COLUMN_LAST_MODIFIED };

    public static boolean openModsDirectory(Activity activity, String expectedModsPath) throws IOException {
        if (!modsRoot(activity).equals(new File(expectedModsPath).getCanonicalFile()))
            throw new IOException("MOD directory does not match the document provider");
        Intent intent = new Intent(Intent.ACTION_VIEW);
        intent.setDataAndType(DocumentsContract.buildRootUri(authority(activity), ROOT_ID),
            "vnd.android.document/root");
        try {
            activity.startActivity(intent);
            return true;
        } catch (ActivityNotFoundException | SecurityException error) {
            Log.w(TITLE, "No compatible document manager", error);
            return false;
        }
    }

    private static String authority(Context context) {
        return context.getPackageName() + ".mods.documents";
    }

    private static File modsRoot(Context context) throws IOException {
        File externalFiles = context.getExternalFilesDir(null);
        if (externalFiles == null) throw new IOException("External app storage is unavailable");
        File root = new File(externalFiles.getCanonicalFile(), "Mods");
        if (!root.equals(root.getCanonicalFile())) throw new IOException("MOD root cannot be a symbolic link");
        if (!root.isDirectory() && !root.mkdirs()) throw new IOException("Cannot create MOD directory");
        return root;
    }

    @Override public boolean onCreate() { return true; }

    @Override public Cursor queryRoots(String[] projection) throws FileNotFoundException {
        rootDirectory();
        MatrixCursor result = new MatrixCursor(projection == null ? ROOT_COLUMNS : projection);
        result.newRow().add(Root.COLUMN_ROOT_ID, ROOT_ID).add(Root.COLUMN_DOCUMENT_ID, ROOT_ID)
            .add(Root.COLUMN_TITLE, TITLE).add(Root.COLUMN_MIME_TYPES, "*/*")
            .add(Root.COLUMN_FLAGS, Root.FLAG_SUPPORTS_CREATE | Root.FLAG_LOCAL_ONLY | Root.FLAG_SUPPORTS_IS_CHILD)
            .add(Root.COLUMN_ICON, getContext().getApplicationInfo().icon);
        return result;
    }

    @Override public Cursor queryDocument(String documentId, String[] projection) throws FileNotFoundException {
        MatrixCursor result = documentCursor(projection, DocumentsContract.buildDocumentUri(authority(getContext()), documentId));
        addDocument(result, resolveDocumentFile(documentId));
        return result;
    }

    @Override public Cursor queryChildDocuments(String parentDocumentId, String[] projection, String sortOrder)
            throws FileNotFoundException {
        MatrixCursor result = documentCursor(projection, childrenUri(parentDocumentId));
        for (File child : listDirectory(resolveDirectory(parentDocumentId))) {
            try { addDocument(result, requireContainedFile(child)); }
            catch (FileNotFoundException ignored) { /* 已移除或符号链接文件不进入系统列表。 */ }
        }
        return result;
    }

    @Override public boolean isChildDocument(String parentDocumentId, String documentId) {
        try { return isDescendant(resolveDirectory(parentDocumentId), resolveDocumentFile(documentId)); }
        catch (FileNotFoundException error) { return false; }
    }

    @Override public ParcelFileDescriptor openDocument(String documentId, String mode, CancellationSignal signal)
            throws FileNotFoundException {
        if (signal != null) signal.throwIfCanceled();
        File file = resolveDocumentFile(documentId);
        if (!file.isFile()) throw new FileNotFoundException("Document is not a file");
        int flags = ParcelFileDescriptor.parseMode(mode);
        if ((flags & ParcelFileDescriptor.MODE_WRITE_ONLY) == 0)
            return ParcelFileDescriptor.open(file, flags);
        try {
            return ParcelFileDescriptor.open(file, flags, new Handler(Looper.getMainLooper()),
                error -> notifyDocumentAndParent(documentId, file.getParentFile()));
        } catch (IOException error) { throw fileFailure(error); }
    }

    @Override public synchronized String createDocument(String parentDocumentId, String mimeType, String displayName)
            throws FileNotFoundException {
        File parent = resolveDirectory(parentDocumentId);
        File created = uniqueDestination(parent, displayName);
        try {
            boolean success = Document.MIME_TYPE_DIR.equals(mimeType) ? created.mkdir() : created.createNewFile();
            if (!success) throw new IOException("Cannot create document");
        } catch (IOException error) { throw fileFailure(error); }
        String id = documentId(created);
        notifyDocumentAndParent(id, parent);
        return id;
    }

    @Override public synchronized String renameDocument(String documentId, String displayName) throws FileNotFoundException {
        File source = resolveMutableDocument(documentId);
        validateDisplayName(displayName);
        if (source.getName().equals(displayName)) return documentId;
        File destination = uniqueDestination(source.getParentFile(), displayName);
        return relocateDocument(documentId, source, destination);
    }

    @Override public synchronized void deleteDocument(String documentId) throws FileNotFoundException {
        File source = resolveMutableDocument(documentId);
        deleteDirectoryTree(source);
    }

    @Override public synchronized String copyDocument(String sourceDocumentId, String targetParentDocumentId)
            throws FileNotFoundException {
        File source = resolveMutableDocument(sourceDocumentId);
        File parent = resolveDirectory(targetParentDocumentId);
        requireSeparateDestination(source, parent);
        File destination = uniqueDestination(parent, source.getName());
        try {
            copyDirectoryTree(source, destination);
        } catch (IOException error) {
            try { if (destination.exists()) deleteDirectoryTree(destination); }
            catch (FileNotFoundException cleanupError) { error.addSuppressed(cleanupError); }
            notifyDocumentAndParent(targetParentDocumentId, parent);
            throw fileFailure(error);
        }
        String id = documentId(destination);
        notifyDocumentAndParent(id, parent);
        return id;
    }

    @Override public synchronized String moveDocument(String sourceDocumentId, String sourceParentDocumentId,
            String targetParentDocumentId) throws FileNotFoundException {
        File source = resolveMutableDocument(sourceDocumentId);
        File parent = resolveDirectory(targetParentDocumentId);
        if (!source.getParentFile().equals(resolveDirectory(sourceParentDocumentId)))
            throw new FileNotFoundException("Source parent has changed");
        requireSeparateDestination(source, parent);
        if (source.getParentFile().equals(parent)) return sourceDocumentId;
        return relocateDocument(sourceDocumentId, source, uniqueDestination(parent, source.getName()));
    }

    private String relocateDocument(String oldId, File source, File destination) throws FileNotFoundException {
        List<String> oldIds = new ArrayList<>();
        collectDirectoryTreeDocumentIds(source, oldIds);
        if (!source.renameTo(destination)) throw new FileNotFoundException("Cannot move or rename document");
        String newId = documentId(destination);
        for (String id : oldIds) revokeDocumentPermission(id);
        getContext().getContentResolver().notifyChange(childrenUri(oldId), null);
        notifyDocumentAndParent(oldId, source.getParentFile());
        notifyDocumentAndParent(newId, destination.getParentFile());
        return newId;
    }

    private File rootDirectory() throws FileNotFoundException {
        try { return modsRoot(getContext()); }
        catch (IOException error) { throw fileFailure(error); }
    }

    private File requireContainedFile(File file) throws FileNotFoundException {
        try {
            File root = rootDirectory();
            File canonical = file.getCanonicalFile();
            if (!file.getAbsoluteFile().equals(canonical) || (!canonical.equals(root) && !isDescendant(root, canonical)))
                throw new FileNotFoundException("Document is outside the MOD directory or uses a symbolic link");
            return canonical;
        } catch (IOException error) { throw fileFailure(error); }
    }

    private File resolveDocumentFile(String id) throws FileNotFoundException {
        File root = rootDirectory();
        File file;
        if (ROOT_ID.equals(id)) file = root;
        else if (id != null && id.startsWith(ROOT_ID + ":"))
            file = requireContainedFile(new File(root, id.substring(ROOT_ID.length() + 1)));
        else throw new FileNotFoundException("Unknown document ID");
        if (!file.exists()) throw new FileNotFoundException("Document no longer exists");
        return file;
    }

    private File resolveDirectory(String id) throws FileNotFoundException {
        File file = resolveDocumentFile(id);
        if (!file.isDirectory()) throw new FileNotFoundException("Document is not a directory");
        return file;
    }

    private File resolveMutableDocument(String id) throws FileNotFoundException {
        File file = resolveDocumentFile(id);
        if (file.equals(rootDirectory())) throw new FileNotFoundException("The MOD root cannot be changed");
        return file;
    }

    private String documentId(File file) throws FileNotFoundException {
        File contained = requireContainedFile(file);
        File root = rootDirectory();
        return contained.equals(root) ? ROOT_ID : ROOT_ID + ":" + contained.getPath().substring(root.getPath().length() + 1);
    }

    private static boolean isDescendant(File parent, File file) {
        return file.getPath().startsWith(parent.getPath() + File.separator);
    }

    private static void requireSeparateDestination(File source, File parent) throws FileNotFoundException {
        if (source.equals(parent) || isDescendant(source, parent))
            throw new FileNotFoundException("Cannot copy or move a directory into itself");
    }

    private static void validateDisplayName(String name) throws FileNotFoundException {
        if (name == null || name.isEmpty() || name.equals(".") || name.equals("..") || name.indexOf('/') >= 0
                || name.indexOf('\\') >= 0 || name.indexOf('\0') >= 0)
            throw new FileNotFoundException("Invalid document name");
    }

    private File uniqueDestination(File parent, String name) throws FileNotFoundException {
        validateDisplayName(name);
        File destination = requireContainedFile(new File(parent, name));
        int extensionIndex = name.lastIndexOf('.');
        String stem = extensionIndex > 0 ? name.substring(0, extensionIndex) : name;
        String extension = extensionIndex > 0 ? name.substring(extensionIndex) : "";
        for (int index = 1; destination.exists(); index++)
            destination = requireContainedFile(new File(parent, stem + " (" + index + ")" + extension));
        return destination;
    }

    private static File[] listDirectory(File directory) throws FileNotFoundException {
        File[] children = directory.listFiles();
        if (children == null) throw new FileNotFoundException("Cannot list directory");
        return children;
    }

    private void deleteDirectoryTree(File file) throws FileNotFoundException {
        String id = documentId(file);
        if (file.isDirectory()) for (File child : listDirectory(file)) deleteDirectoryTree(child);
        if (!file.delete()) throw new FileNotFoundException("Cannot delete document");
        revokeDocumentPermission(id);
        notifyDocumentAndParent(id, file.getParentFile());
    }

    private void collectDirectoryTreeDocumentIds(File file, List<String> ids) throws FileNotFoundException {
        ids.add(documentId(file));
        if (file.isDirectory()) for (File child : listDirectory(file)) collectDirectoryTreeDocumentIds(child, ids);
    }

    private void copyDirectoryTree(File source, File destination) throws IOException {
        requireContainedFile(source);
        if (source.isDirectory()) {
            if (!destination.mkdir()) throw new IOException("Cannot create destination directory");
            for (File child : listDirectory(source)) copyDirectoryTree(child, new File(destination, child.getName()));
        } else {
            if (!destination.createNewFile()) throw new IOException("Destination already exists");
            try (FileInputStream input = new FileInputStream(source); FileOutputStream output = new FileOutputStream(destination)) {
                byte[] buffer = new byte[32768];
                int count;
                while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
            }
        }
    }

    private MatrixCursor documentCursor(String[] projection, Uri notificationUri) {
        MatrixCursor cursor = new MatrixCursor(projection == null ? DOCUMENT_COLUMNS : projection);
        cursor.setNotificationUri(getContext().getContentResolver(), notificationUri);
        return cursor;
    }

    private void addDocument(MatrixCursor cursor, File file) throws FileNotFoundException {
        boolean directory = file.isDirectory();
        boolean root = file.equals(rootDirectory());
        int flags = directory ? Document.FLAG_DIR_SUPPORTS_CREATE : Document.FLAG_SUPPORTS_WRITE;
        if (!root) {
            flags |= Document.FLAG_SUPPORTS_DELETE | Document.FLAG_SUPPORTS_RENAME;
            if (Build.VERSION.SDK_INT >= 24) flags |= Document.FLAG_SUPPORTS_COPY | Document.FLAG_SUPPORTS_MOVE;
        }
        cursor.newRow().add(Document.COLUMN_DOCUMENT_ID, documentId(file))
            .add(Document.COLUMN_DISPLAY_NAME, root ? TITLE : file.getName())
            .add(Document.COLUMN_MIME_TYPE, documentMimeType(file)).add(Document.COLUMN_FLAGS, flags)
            .add(Document.COLUMN_SIZE, directory ? 0 : file.length())
            .add(Document.COLUMN_LAST_MODIFIED, file.lastModified());
    }

    private static String documentMimeType(File file) {
        if (file.isDirectory()) return Document.MIME_TYPE_DIR;
        String name = file.getName();
        String type = MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(name.lastIndexOf('.') + 1).toLowerCase(Locale.ROOT));
        return type == null ? "application/octet-stream" : type;
    }

    private Uri childrenUri(String parentId) {
        return DocumentsContract.buildChildDocumentsUri(authority(getContext()), parentId);
    }

    private void notifyDocumentAndParent(String id, File parent) {
        getContext().getContentResolver().notifyChange(DocumentsContract.buildDocumentUri(authority(getContext()), id), null);
        try {
            getContext().getContentResolver().notifyChange(childrenUri(documentId(parent)), null);
        } catch (FileNotFoundException ignored) { /* 文件管理器中的旧请求可能晚于父目录删除。 */ }
    }

    private static FileNotFoundException fileFailure(IOException error) {
        return (FileNotFoundException) new FileNotFoundException(error.getMessage()).initCause(error);
    }
}
