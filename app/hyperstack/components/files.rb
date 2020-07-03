# frozen_string_literal: true

# Single item in the files view
class FileItem < BaseFileItem
  param :file
  param :path_so_far

  def name
    @File.name.to_s
  end

  def path
    @PathSoFar
  end

  render(TR) do
    # TODO: icon based on type
    TD { @File.folder? ? @File.ftype_pretty : '' }

    # Clickability
    TD {
      # Item opening logic works for everything
      A(href: basic_url_join(@BasePath, name)) { name }.on(:click) { |e|
        e.prevent_default
        @OnNavigate.call full_path
      }
    }

    TD { @File.size_readable }
    TD { @File.read_access_pretty }
  end
end

# Shows stored files
class Files < HyperComponent
  include Hyperstack::Router::Helpers

  before_mount do
    @parsed_path = ''
    @last_parsed_path = nil
    @recalculating_path = nil
    @current_folder = nil
    @show_item_sidebar = nil
    @remove_from_end = ''

    @can_upload = false

    @show_folder_info = false
    @folder_delete_started = false
    @folder_delete_text = ''

    @show_new_folder_create = false
    @new_folder_name = ''
    @new_folder_read_access = 'developers'
    @new_folder_write_access = 'developers'

    @show_upload_overlay = false
    @upload_in_progress = nil
    @total_files_to_upload = 0
    @total_uploaded = 0
    @upload_errors = []
    @item_copy_link_text = nil

    @upload_selected_files = nil
  end

  # Returns items in current folder, because the path parsing is done on the server this
  # can't use the given path parameter
  def files(_path)
    StorageItem.by_folder(@current_folder&.id).visible_to(App.acting_user&.id).folder_sort
  end

  def raw_path
    match.params[:path]
  end

  def item_preview(item, url)
    ext = File.extname item.name

    DIV(class: 'PreviewBox') {
      case ext
      when '.png', '.jpg', '.jpeg'
        IMG(src: url, class: 'PreviewImage')
      when '.mkv', '.webm', '.mp4'
        VIDEO(src: url, class: 'PreviewVideo', controls: true)
        # TODO: this probably needs to download the data for showing
        # when '.json', '.md', '.txt'
        # IFRAME(src: url)
      when '.html'
        IFRAME(src: url)
      else
        SPAN { "No preview available for this file type (#{ext})" }
      end
    }
  end

  def item_information(item)
    H3 { 'Information' }

    DIV { "Name: #{item.name}" }
    DIV { "Type: #{item.ftype_pretty}" }
    DIV { "ID: #{item.id}" }
    DIV { "Can be parentless: #{item.allow_parentless}" }
    DIV { "Size: #{item.size}" }
    DIV { "Read: #{item.read_access_pretty}" }
    DIV { "Write: #{item.write_access_pretty}" }
    DIV { "Owner: #{item.owner&.name_or_id}" } if App.acting_user
    DIV { "Parent folder: #{item.parent&.name}" }
    DIV { "Special: #{item.special}" }
    DIV { "Created: #{item.created_at}" }
    DIV { "Updated: #{item.updated_at}" }
  end

  def show_current_item
    DIV(class: 'ItemSidebar', key: 'sidebar') {
      can_close = !@upload_in_progress && !@show_upload_overlay
      RS.Button(class: 'close', style: { fontSize: '2.3rem' }, disabled: !can_close) {
        SPAN(dangerously_set_inner_HTML: { __html: '&times;' })
      }.on(:click) {
        @file_browser.change_folder @parsed_path if can_close
      }

      H2 { @show_item_sidebar.name.to_s }

      download_abs = "#{Window.location.scheme}//#{Window.location.host}" \
                     "/api/v1/download/#{@show_item_sidebar.id}"

      item_preview @show_item_sidebar, download_abs

      A(href: download_abs, target: '_blank') { 'Download' }

      P { @item_copy_link_text.to_s } if @item_copy_link_text
      BR {}
      INPUT(style: { display: 'none' }, type: :text, value: download_abs, ref: set(:link_for_dl))
      RS.Button(size: 'sm', disabled: true) {
        'Get Download Link'
      }.on(:click) { |event|
        event.prevent_default
        link = @link_for_dl
        `console.log(link)`
        link.select
        `link.select()`
        `document.execCommand("copy")`
        @item_copy_link_text = 'Copied to clipboard'
      }
      RS.Button(size: 'sm', class: 'LeftMargin', disabled: true) {
        'Get Item Link'
      }.on(:click, &:prevent_default)

      item_information @show_item_sidebar

      H3 { 'Versions' }
      RS.Table(:striped, :responsive) {
        THEAD {
          TR {
            TH { 'Version' }
            TH { 'Size' }
            TH { 'Keep' }
            TH { 'Protected' }
            TH { 'Uploaded' }
            TH { 'Actions' }
            TH { 'Date' }
          }
        }
        TBODY {
          @show_item_sidebar.storage_item_versions.each { |version|
            TR(key: version.version.to_s) {
              TD {
                A(href: "/api/v1/download/#{@show_item_sidebar.id}?version=#{version.version}",
                  target: '_blank') { version.version.to_s }
              }
              TD { "#{version.size_mib} MiB" }
              TD { version.keep.to_s }
              TD { version.protected.to_s }
              TD { (!version.uploading).to_s }
              TD {}
              TD { version.created_at.to_s }
            }
          }
        }
      }
    }
  end

  def upload_overlay
    DIV(class: 'BlockOverlay') {
      unless @can_upload
        RS.Alert(color: 'danger') { "You don't have write access to this folder" }
      end

      @upload_errors.each { |error|
        P { "Upload error: #{error}" }
      }

      if @upload_in_progress
        DIV {
          SPAN { 'Uploading ' }
          RS.Spinner(color: 'primary')
        }

        BR {}
      end

      P {
        'Drop files here to upload'
      }

      BR(style: { marginTop: '25px' })

      RS.Form() {
        RS.Label {
          'Or select files to upload:'
        }
        BR {}
        INPUT(type: :file, name: 'files', multiple: true).on(:change) { |event|
          if event.target.files.empty?
            mutate @upload_selected_files = nil
          else
            mutate @upload_selected_files = `[...event.native.target.files]`
          end
        }

        BR {}
        BR {}

        RS.Button(type: 'submit', color: 'primary',
                  disabled: !@upload_selected_files || !@can_upload) {
          'Upload Files'
        }.on(:click) { |event|
          event.prevent_default
          begin_file_upload @upload_selected_files
        }
        RS.Button(class: 'LeftMargin') {
          'Cancel'
        }.on(:click) { |event|
          event.prevent_default
          mutate @show_upload_overlay = false
        }
      }
    }.on(:DragLeave) { |event|
      event.prevent_default
      event.stop_propagation
      mutate { @show_upload_overlay = false } if @show_upload_overlay
    }.on(:DragOver) { |event|
      event.prevent_default
      event.stop_propagation
      mutate { @show_upload_overlay = true } unless @show_upload_overlay
    }.on(:Drop) { |event|
      event.prevent_default
      event.stop_propagation
      break unless @can_upload

      files = `[...event.native.dataTransfer.files]`
      begin_file_upload files
    }
  end

  def folder_action_buttons
    new_access = FilePermissions.has_access?(
      App.acting_user,
      !@current_folder.nil? ? @current_folder.write_access : ITEM_ACCESS_OWNER,
      @current_folder&.owner ? @current_folder&.owner&.id : nil
    )

    mutate @can_upload = new_access if @can_upload != new_access

    if @folder_operation_in_progress
      RS.Spinner(color: 'primary')
      BR {}
    end

    P { @folder_operation_result.to_s } if @folder_operation_result

    if @can_upload || App.acting_user
      RS.Button(color: 'primary', disabled: !@can_upload) { 'Upload' }.on(:click) {
        mutate @show_upload_overlay = true
      }

      RS.Button(color: 'info', class: 'LeftMargin') {
        (@show_folder_info ? 'Hide' : 'Show') + ' Folder Info'
      } .on(:click) {
        mutate @show_folder_info = !@show_folder_info
      }

      RS.Button(color: 'success', disabled: !@can_upload, class: 'LeftMargin') {
        'New Folder'
      } .on(:click) {
        mutate {
          @show_new_folder_create = !@show_new_folder_create
          @new_folder_name = ''
          @new_folder_read_access = if @current_folder
                                      @current_folder.read_access_pretty
                                    else
                                      'developers'
                                    end
          @new_folder_write_access = if @current_folder
                                       @current_folder.write_access_pretty
                                     else
                                       'developers'
                                     end
        }
      }

      if @current_folder
        can_delete = @can_upload && App.acting_user && !@current_folder.special &&
                     @current_folder.folder_entries.count < 1

        RS.Button(color: 'danger', disabled: !can_delete, class: 'LeftMargin') {
          'Delete This Folder'
        } .on(:click) {
          mutate {
            @folder_delete_started = !@folder_delete_started
            @folder_delete_text = ''
          }
        }
      end
    end

    if @folder_delete_started
      P { "Deleting a folder can't be undone. Only an empty folder can be deleted." }
      SPAN { "Type in the folder name (#{@current_folder.name}), to delete: " }
      RS.Input(type: :text, value: @folder_delete_text,
               placeholder: 'Folder name').on(:change) { |e|
        mutate {
          @folder_delete_text = e.target.value
        }
      }

      RS.Button(color: 'danger', disabled: @folder_delete_text != @current_folder.name) {
        'Delete'
      } .on(:click) {
        mutate {
          @folder_delete_started = false
          @folder_operation_in_progress = true
          @folder_operation_result = ''
        }

        DeleteFolder.run(folder_id: @current_folder.id).then {
          mutate {
            @folder_operation_in_progress = false
            @folder_operation_result = 'Folder deleted. Please move to the parent folder.'
          }
        }.fail { |error|
          mutate {
            @folder_operation_in_progress = false
            @folder_operation_result = "Error: #{error}"
          }
        }
      }
    end

    if @show_new_folder_create
      RS.Form {
        RS.FormGroup {
          RS.Label { 'Name for new folder' }
          RS.Input(type: :text, placeholder: 'Name', value: @new_folder_name).on(:change) { |e|
            mutate {
              @new_folder_name = e.target.value
            }
          }
        }

        RS.Row(form: true) {
          RS.Col(md: 6, sm: 12) {
            RS.FormGroup {
              RS.Label { 'Read access' }
              RS.Input(type: :select, placeholder: 'Name', value: @new_folder_read_access) {
                OPTION { 'public' }
                OPTION { 'users' }
                OPTION { 'developers' }
                OPTION { 'owner' }
              }.on(:change) { |e|
                mutate {
                  @new_folder_read_access = e.target.value
                }
              }
            }
          }
          RS.Col(md: 6, sm: 12) {
            RS.FormGroup {
              RS.Label { 'Write access' }
              RS.Input(type: :select, placeholder: 'Name', value: @new_folder_write_access) {
                OPTION { 'public' }
                OPTION { 'users' }
                OPTION { 'developers' }
                OPTION { 'owner' }
              }.on(:change) { |e|
                mutate {
                  @new_folder_write_access = e.target.value
                }
              }
            }
          }
        }

        RS.Button(type: 'submit', color: 'primary', disabled: @new_folder_name.blank?) {
          'Create'
        }.on(:click) { |event|
          event.prevent_default
          mutate {
            @folder_operation_in_progress = true
            @folder_operation_result = ''
          }

          CreateNewFolder.run(parent_folder_id: @current_folder&.id, name: @new_folder_name,
                              read_access: @new_folder_read_access,
                              write_access: @new_folder_write_access).then {
            mutate {
              @show_new_folder_create = false
              @folder_operation_in_progress = false
              @folder_operation_result = ''
            }
          }.fail { |error|
            mutate {
              @folder_operation_in_progress = false
              @folder_operation_result = "Error: #{error}"
            }
          }
        }
        RS.Button(color: 'secondary', class: 'LeftMargin') {
          'Cancel'
        }.on(:click) { |event|
          event.prevent_default
          mutate {
            @show_new_folder_create = false
            @new_folder_name = ''
          }
        }
      }
      # @new_folder_name = ''
      # @ = 2
      # @new_folder_write_access = 2
    end

    if @show_folder_info
      if !@current_folder
        P { 'Root folder' }
      else
        item_information @current_folder
      end
    end
  end

  render(DIV) do
    H2 { 'Files' }
    P {
      "This is a service to store files needed for Thrive development, which aren't "\
      'included in the code repository.'
    }

    if @last_parsed_path != raw_path && @recalculating_path != raw_path
      start_parsing_path
      return
    end

    if @path_parse_failure
      RS.Alert(color: 'danger') { "Error parsing path: #{@path_parse_failure}" }
      return
    end

    if @parsing_path
      RS.Spinner(color: 'primary')
      return
    end

    show_current_item if @show_item_sidebar

    DIV(class: 'BlockContainer') {
      upload_overlay if @show_upload_overlay || @upload_in_progress

      FileBrowser(
        read_path: -> { raw_path },
        location_pathname: -> { location.pathname },
        root_path_name: 'Files',
        items: ->(path) { files path },
        thead: lambda {
          THEAD {
            TR {
              TH { 'Type' }
              TH { 'Name' }
              TH { 'Size' }
              TH { 'Access' }
            }
          }
        },
        item_create: lambda { |item, base|
          FileItem(file: item, base_path: base.chomp(@remove_from_end),
                   path_so_far: @parsed_path, key: item.name)
        },
        column_count_for_empty: 4,
        column_empty_indicator: 1,
        key: 'filebrowser',
        ref: set(:file_browser)
      ).on(:change_folder) { |folder|
        history.push folder
      }
    }.on(:DragEnter) { |event|
      event.prevent_default
      event.stop_propagation
      mutate { @show_upload_overlay = true } unless @show_upload_overlay
    }

    folder_action_buttons
  end

  private

  # Parses the current path
  def start_parsing_path
    current_path = raw_path
    mutate {
      @recalculating_path = current_path
      @parsing_path = true
      @path_parse_failure = nil
      @can_upload = false
    }
    ProcessStoragePath.run(path: current_path).then { |path, current_item, error|
      if !error.blank?
        mutate {
          @recalculating_path = nil
          @last_parsed_path = current_path
          @path_parse_failure = error.to_s
          @parsing_path = false
        }
      else
        fetched = path.map { |i|
          StorageItem.find i
        }

        Promise.when(*fetched.map { |i|
                       i.load(
                         :name, :read_access, :write_access, :ftype
                       )
                     }).then {
          fetched_folders = fetched.select(&:folder?)
          folder = fetched_folders.last

          mutate {
            if !current_item.nil?
              @show_item_sidebar = StorageItem.find current_item

              # This is chomped from paths to make navigation work while an item is open
              @remove_from_end = "/#{@show_item_sidebar.name}"
            else
              @remove_from_end = ''
            end

            @recalculating_path = nil
            @last_parsed_path = current_path
            @current_folder = folder
            @parsing_path = false
            @parsed_path = fetched_folders.map(&:name).join('/')
          }
        }
      end
    }.fail { |error|
      mutate {
        @recalculating_path = nil
        @last_parsed_path = current_path
        @parsing_path = false
        @path_parse_failure = "Request to parse path failed: #{error}"
      }
    }
  end

  def begin_file_upload(files)
    mutate {
      @upload_in_progress = true
      @upload_selected_files = nil
      @total_files_to_upload += files.length
    }

    upload_batch files
  end

  def upload_batch(files)
    return if files.empty?

    Promise.when(upload_files(files[0..0].first)).then { upload_batch(files[1..-1]) }
  end

  # rubocop:disable Lint/UnusedMethodArgument
  # rubocop:disable Lint/UnusedBlockArgument
  def upload_files(file)
    name = `file.name`
    RequestStartUpload.run(
      folder_id: @current_folder&.id,
      size: `file.size`,
      file_name: name,
      mime_type: `file.type`
    ).then { |url, data, key|
      puts "Starting file send to: #{url}"

      promise = Promise.new
      %x{
        let formData = new FormData();
        formData.append('key', #{data['key']});
        formData.append('policy', #{data['policy']});
        formData.append('x-amz-credential', #{data['x-amz-credential']});
        formData.append('x-amz-algorithm', #{data['x-amz-algorithm']});
        formData.append('x-amz-date', #{data['x-amz-date']});
        formData.append('x-amz-signature', #{data['x-amz-signature']});
        formData.append('Content-Type', file.type);
        formData.append('file', file);
        fetch(url, {
          method: 'POST',
          body: formData
        })
          .then(response => {#{promise.resolve(`response`)}})
          .catch(error => {#{promise.reject(`error`)}})
      }

      promise.then { |response|
        response_status = `response.status`

        if `response.ok` != true
          `console.log('failed response:', response)`
          raise "Invalid response from storage PUT request, status: #{response_status}"
        end

        ReportFinishedUpload.run(upload_token: key)
      }
    }.then {
      # Succeeded
      mutate {
        @total_uploaded += 1
        @upload_in_progress = false if @total_uploaded >= @total_files_to_upload

        # Close uploader if all succeeded
        @show_upload_overlay = false if @upload_errors.empty? && !@upload_in_progress
      }
    }.fail { |error|
      puts "Upload error: #{error}"
      mutate @upload_errors += ["#{name} - #{error.message}"]
    }
  end
  # rubocop:enable Lint/UnusedMethodArgument
  # rubocop:enable Lint/UnusedBlockArgument
end
