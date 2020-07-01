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
    @current_folder = nil
    @show_item_sidebar = nil
    @remove_from_end = ''

    @show_upload_overlay = false
    @upload_in_progress = nil
    @total_files_to_upload = 0
    @total_uploaded = 0
    @upload_errors = []

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

  def show_current_item
    DIV(class: 'ItemSidebar', key: 'sidebar') {
      RS.Button(class: 'close', style: { fontSize: '2.3rem' }) {
        SPAN(dangerously_set_inner_HTML: { __html: '&times;' })
      }.on(:click) {
        @file_browser.change_folder @parsed_path
      }

      H2 { @show_item_sidebar.name.to_s }

      H3 { 'Information' }

      DIV { "Name: #{@show_item_sidebar.name}" }
      DIV { "Type: #{@show_item_sidebar.ftype_pretty}" }
      DIV { "ID: #{@show_item_sidebar.id}" }
      DIV { "Can be parentless: #{@show_item_sidebar.allow_parentless}" }
      DIV { "Size: #{@show_item_sidebar.size}" }
      DIV { "Read: #{@show_item_sidebar.read_access_pretty}" }
      DIV { "Write: #{@show_item_sidebar.write_access_pretty}" }
      DIV { "Owner: #{@show_item_sidebar.owner&.name_or_id}" } if App.acting_user
      DIV { "Parent folder: #{@show_item_sidebar.parent&.name}" }
      DIV { "Created: #{@show_item_sidebar.created_at}" }
      DIV { "Updated: #{@show_item_sidebar.updated_at}" }

      H3 { 'Versions' }
      RS.Table(:striped, :responsive) {
        THEAD{
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
        TBODY{
          @show_item_sidebar.storage_item_versions.each{|version|
            TR(key: version.version.to_s) {
              TD{version.version.to_s}
              TD{"#{version.size_mib} MiB"}
              TD{version.keep.to_s}
              TD{version.protected.to_s}
              TD{(!version.uploading).to_s}
              TD{}
              TD{version.created_at.to_s}
            }
          }
        }
      }
    }
  end

  render(DIV) do
    H2 { 'Files' }
    P {
      "This is a service to store files needed for Thrive development, which aren't "\
      'included in the code repository.'
    }

    if @last_parsed_path != raw_path && @recalculating_path != raw_path
      current_path = raw_path
      # Need to parse the path
      mutate {
        @recalculating_path = current_path
        @parsing_path = true
        @path_parse_failure = nil
      }
      ProcessStoragePath.run(path: current_path).then { |path, current_item, error|
        mutate {
          @recalculating_path = nil
          @last_parsed_path = current_path
          @parsing_path = false
          if !error.blank?
            @path_parse_failure = error.to_s
          else
            re_created = path.map { |i|
              item = StorageItem.find i
            }

            folders = re_created.select(&:folder?)

            @parsed_path = folders.map(&:name).join('/')
            @current_folder = folders.last

            if !current_item.nil?
              @show_item_sidebar = StorageItem.find current_item

              # This is chomped from paths to make navigation work while an item is open
              @remove_from_end = "/#{@show_item_sidebar.name}"
            else
              @remove_from_end = ''
            end
          end
        }
      }.fail { |error|
        mutate {
          @recalculating_path = nil
          @last_parsed_path = current_path
          @parsing_path = false
          @path_parse_failure = "Request to parse path failed: #{error}"
        }
      }
    end

    if @path_parse_failure
      RS.Alert(color: 'danger') { "Error parsing path: #{@path_parse_failure}" }
    end

    if @parsing_path
      RS.Spinner(color: 'primary')
      return
    end

    show_current_item if @show_item_sidebar

    DIV(class: 'BlockContainer') {
      if @show_upload_overlay || @upload_in_progress
        DIV(class: 'BlockOverlay') {
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

            RS.Button(type: 'submit', color: 'primary', disabled: !@upload_selected_files) {
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
          files = `[...event.native.dataTransfer.files]`
          begin_file_upload files
        }
      end
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

    can_upload = FilePermissions.has_access?(
      App.acting_user,
      @current_folder ? @current_folder.write_access : ITEM_ACCESS_OWNER,
      @current_folder&.owner_id
    )

    if can_upload || App.acting_user
      RS.Button(color: 'primary', disabled: !can_upload) { 'Upload' }.on(:click) {
        mutate @show_upload_overlay = true
      }

      RS.Button(color: 'success', disabled: !can_upload, class: 'LeftMargin') {
        'New Folder'
      } .on(:click) {
      }
    end
  end

  private

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
      file_name: name
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
      mutate @upload_errors += ["#{name} - #{error}"]
    }
  end
  # rubocop:enable Lint/UnusedMethodArgument
  # rubocop:enable Lint/UnusedBlockArgument
end
