// File preview functionality
$(document).ready(function() {
    $('#modalAttachment').on('change', function() {
        const file = this.files[0];
        if (file) {
            // File size validation
            if (file.size > 5 * 1024 * 1024) { // 5MB
                alert('File size must be less than 5MB');
                this.value = '';
                $('#filePreview').addClass('d-none');
                return;
            }
            
            // Create the preview based on file type
            const isImage = file.type.startsWith('image/');
            
            if (isImage) {
                // Preview image
                const reader = new FileReader();
                reader.onload = function(e) {
                    // Create the image element preview
                    $('#filePreviewContent').html(`
                        <img src="${e.target.result}" alt="Preview" class="img-thumbnail mx-auto" 
                                style="max-height: 250px; max-width: 100%; border-radius: 8px; box-shadow: 0 3px 10px rgba(0,0,0,0.1);">
                    `);
                    
                    // Show the preview container
                    $('#filePreview').removeClass('d-none');
                };
                reader.readAsDataURL(file);
            } else {
                // Create file info preview
                let fileIcon = 'fas fa-file';
                
                // Set icon based on file type
                if (file.type.includes('pdf')) fileIcon = 'fas fa-file-pdf';
                else if (file.type.includes('word') || file.type.includes('document')) fileIcon = 'fas fa-file-word';
                else if (file.type.includes('excel') || file.type.includes('sheet')) fileIcon = 'fas fa-file-excel';
                else if (file.type.includes('video')) fileIcon = 'fas fa-file-video';
                else if (file.type.includes('audio')) fileIcon = 'fas fa-file-audio';
                else if (file.type.includes('zip') || file.type.includes('archive')) fileIcon = 'fas fa-file-archive';
                
                // Create file preview HTML
                $('#filePreviewContent').html(`
                    <div class="file-preview p-3 text-center">
                        <i class="${fileIcon} fa-3x mb-2 text-primary"></i>
                        <p class="mb-0">${file.name}</p>
                        <p class="text-muted small">${(file.size/1024).toFixed(1)} KB</p>
                    </div>
                `);
                
                // Show the preview container
                $('#filePreview').removeClass('d-none');
            }
        }
    });
    
    // Remove file button
    $(document).on('click', '#removeFile', function() {
        $('#modalAttachment').val('');
        $('#filePreview').addClass('d-none');
    });
}); 