# frozen_string_literal: true

class FileItem < BaseFileItem
  param :file

  def name
    @File.name.to_s
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

    if @last_parsed_path != raw_path
      current_path = raw_path
      # Need to parse the path
      mutate {
        @parsing_path = true
        @path_parse_failure = ''
      }
      ProcessStoragePath.run(path: current_path).then { |path, current_item, error|
        mutate {
          @last_parsed_path = current_path
          @parsing_path = false
          if !error.blank?
            @path_parse_failure = error.to_s
          else
            @parsed_path = path.map(&:name).join('/')
            @current_folder = path.last
            @show_item_sidebar = current_item
          end
        }
      }.fail { |error|
        mutate {
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
        puts "Creating item: #{item}"
        FileItem(file: item, base_path: base)
      },
      column_count_for_empty: 4,
      column_empty_indicator: 1
    ).on(:change_folder) { |folder|
      history.push folder
    }

    if App.acting_user&.developer?
      RS.Button(color: 'success', disabled: true) { 'New Folder' }.on(:click) {}
    end
  end
end
