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
    TD { @File.folder? ? 'folder' : '' }

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
    @parsed_path = '/'
    @current_folder = nil
    @show_item_sidebar = nil

    @show_upload_overlay = false
    @upload_in_progress = nil

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
            # TODO: maybe there is an inbuilt way to do this?
            re_created = path.map { |i|
              item = StorageItem.new i
              item.id = i['id']
              item
            }
            @parsed_path = re_created.join('/')
            @current_folder = re_created.last
            @show_item_sidebar = current_item
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

    P { "Error parsing path: #{@path_parse_failure}" } if @path_parse_failure

    if @parsing_path
      RS.Spinner(color: 'primary')
      return
    end

    P { "Selected item: #{@show_item_sidebar}" } if @show_item_sidebar

    DIV(class: 'BlockContainer') {
      if @show_upload_overlay
        DIV(class: 'BlockOverlay') {
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
                mutate @upload_selected_files = event.target.files
              end
            }

            BR{}
            BR{}

            RS.Button(type: 'submit', color: 'primary', disabled: !@upload_selected_files) {
              'Upload Files'
            }.on(:click, &:prevent_default)
            RS.Button(class: 'LeftMargin') {
              'Cancel'
            }.on(:click) { |event|
              event.prevent_default
              mutate @show_upload_overlay = false
            }
          }
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
          FileItem(file: item, base_path: base, path_so_far: raw_path, key: item.name)
        },
        column_count_for_empty: 4,
        column_empty_indicator: 1,
        key: 'filebrowser'
      ).on(:change_folder) { |folder|
        history.push folder
      }
    }.on(:DragEnter) { |event|
      event.prevent_default
      mutate @show_upload_overlay = true
    }.on(:DragLeave) { |event|
      event.prevent_default
      mutate @show_upload_overlay = false
    }.on(:DragOver) { |event|
      event.prevent_default
      puts "received drag over: #{event.files.count}"
      mutate @show_upload_overlay = true if @show_upload_overlay
    }.on(:Drop) { |event|
      event.prevent_default
      puts "received drop: #{event}"
      # begin_file_upload
    }

    can_upload = FilePermissions.has_access?(
      App.acting_user,
      @current_folder ? @current_folder.write_access : ITEM_ACCESS_OWNER, @current_folder&.owner
    )

    RS.Button(color: 'primary', disabled: !can_upload) { 'Upload' }.on(:click) {
      mutate @show_upload_overlay = true
    }

    if App.acting_user&.developer?
      RS.Button(color: 'success', disabled: !can_upload, class: 'LeftMargin') {
        'New Folder'
      } .on(:click) {
      }
    end
  end

  def begin_file_upload(files); end
end
