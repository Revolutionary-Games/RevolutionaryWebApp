# frozen_string_literal: true

# Base type with some helpers for items used by FileBrowser
class BaseFileItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :on_navigate, type: Proc
  param :base_path, type: String

  def full_path
    if path == '/'
      "/#{name}"
    else
      "#{path}/#{name}"
    end
  end
end

# A file browser component that can be customized for different data sources
class FileBrowser < HyperComponent
  param :read_path, type: Proc
  param :location_pathname, type: Proc
  param :item_create, type: Proc
  param :root_path_name, type: String
  param :items, type: Proc
  param :on_change_folder, type: Proc
  param :thead, type: Proc
  param :column_count_for_empty, type: Integer
  param :column_empty_indicator, type: Integer

  before_mount do
    @file_path = @ReadPath.call

    @file_path = '/' if @file_path.blank?

    @file_path = '/' + @file_path if @file_path[0] != '/'

    @file_page = 0
  end

  # Calculates the final path to change to based on navigation target
  def folder_change_path(folder)
    base = @LocationPathname.call

    # The pathname includes existing folders we are in so we need to strip that
    base = base.sub(@ReadPath.call, '') if @ReadPath.call

    basic_url_join(base, folder)
  end

  def change_folder(folder)
    @OnChangeFolder.call folder_change_path folder
  end

  # Shows using breadcrumbs the current folder
  def breadcrumbs
    parts = @file_path.split('/').delete_if(&:blank?)

    DIV {
      RS.Breadcrumb {
        RS.BreadcrumbItem(active: parts.empty?) {
          if parts.empty?
            SPAN { @RootPathName }
          else
            A(href: folder_change_path('/')) {
              @RootPathName
            }.on(:click) { |e|
              e.prevent_default
              change_folder '/'
            }
          end
        }
        parts.each_with_index { |part, index|
          active = index == parts.size - 1
          target = parts[0..index].join('/')
          RS.BreadcrumbItem(active: active) {
            if active
              SPAN { part }
            else
              A(href: folder_change_path(target)) {
                part
              }.on(:click) { |e|
                e.prevent_default
                change_folder target
              }
            end
          }
        }
      }
    }
  end

  render(DIV) do
    breadcrumbs

    items = @Items.call @file_path

    Paginator(current_page: @file_page,
              page_size: 100,
              item_count: items.count,
              ref: set(:paginator)) {
      # This is set with a delay
      if @paginator
        RS.Table(:striped, :responsive) {
          @Thead.call

          TBODY {
            created_items = 0
            items.paginated(@paginator.offset, @paginator.take_count).each { |item|
              item = @ItemCreate.call(item, @LocationPathname.call)

              next if item.nil?

              created_items += 1

              item.on(:navigate) { |path|
                change_folder path
              }
            }

            if created_items < 1
              TR{
                (1..@ColumnCountForEmpty).each_with_index { |_, i|
                  TD{
                    "This folder is empty" if i == @ColumnEmptyIndicator
                  }
                }
              }
            end
          }
        }
      end
    }.on(:page_changed) { |page|
      mutate { @file_page = page }
    }.on(:created) {
      mutate {}
    }
  end
end
