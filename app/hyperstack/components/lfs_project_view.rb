# frozen_string_literal: true

def basic_url_join(base, added)
  slash1 = base[-1] == '/'
  slash2 = added[0] == '/'
  if slash1 && slash2
    base + added[1..-1]
  elsif slash1 || slash2
    base + added
  else
    base + '/' + added
  end
end

# Table row
class LFSObjectItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :object
  render(TR) do
    TH(scope: 'row') { @Object.oid.to_s }
    TD { @Object.size_mib.to_s + ' MiB' }
    TD { @Object.created_at.to_s }
  end
end

class LFSGitFileItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :on_navigate, type: Proc
  param :file
  param :base_path, type: String

  def name
    @File.name.to_s
  end

  def path
    @File.path.to_s
  end

  def full_path
    if @File.path == '/'
      "/#{name}"
    else
      "#{path}/#{name}"
    end
  end

  render(TR) do
    # TODO: icon based on type
    TD { @File.folder? ? @File.ftype.to_s : '' }

    # Clickability
    TD {
      if @File.folder?
        # Folder browsing
        A(href: basic_url_join(@BasePath, name)) { name }.on(:click) { |e|
          e.prevent_default
          @OnNavigate.call full_path
        }
      elsif @File.lfs?
        # Link to our git lfs API single endpoint
        encoded_name = `encodeURIComponent(#{name})`
        encoded_path = `encodeURIComponent(#{path})`
        A(href: "/api/v1/lfs_file?project=#{@File.lfs_project.id}&" \
                "path=#{encoded_path}&name=#{encoded_name}",
          target: '_blank') { name }
      else
        # Link to repo url
        A(href: "#{@File.lfs_project.repo_url}/tree/master#{full_path}",
          target: '_blank') { name }
      end
    }

    TD { @File.size_readable }
    TD { @File.lfs?.to_s }
  end
end

# Shows a single LFS project
class LfsProjectView < HyperComponent
  include Hyperstack::Router::Helpers

  param :current_raw_page, default: 0, type: Integer

  def raw_items
    LfsObject.by_project(@project.id).sort_by_created_at
  end

  def files
    ProjectGitFile.by_project(@project.id).with_path(@file_path).sort_by_name
  end

  def finish_editing
    mutate {
      @show_editing_spinner = true
      @editing_status_text = ''
    }

    UpdateLfsProjectUrls.run(
      project_id: @project.id, repo_url: @editing_repo_url,
      clone_url: @editing_clone_url
    ).then {
      mutate {
        @editing_project = false
        @show_editing_spinner = false
      }
    }.fail { |error|
      mutate {
        @find_campaign_id_status = "Failed to save, error: #{error}"
        @show_editing_spinner = false
      }
    }
  end

  before_mount do
    @show_raw_objects = false

    # Editing the project
    @editing_project = false
    @editing_status_text = ''
    @show_editing_spinner = false
    @editing_repo_url = ''
    @editing_clone_url = ''
    @edited_project = false

    @refresh_message = ''
    @refresh_pressed = false
    @rebuild_pressed = false

    @project = LfsProject.find_by slug: match.params[:slug]

    # File parts
    @file_path = match.params[:path]

    @file_path = '/' if @file_path.blank?

    @file_path = '/' + @file_path if @file_path[0] != '/'

    @file_page = 0
  end

  def folder_change_path(folder)
    base = location.pathname

    # The pathname includes existing folders we are in so we need to strip that
    base = base.sub(match.params[:path], '') if match.params[:path]

    basic_url_join(base, folder)
  end

  def change_folder(folder)
    history.push folder_change_path folder
  end

  # Shows using breadcrumbs the current folder
  def breadcrumbs
    parts = @file_path.split('/').delete_if(&:blank?)

    DIV {
      RS.Breadcrumb {
        RS.BreadcrumbItem(active: parts.empty?) {
          if parts.empty?
            SPAN { @project.name.to_s }
          else
            A(href: folder_change_path('/')) {
              @project.name.to_s
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
    unless @project
      H1 { 'No project found' }
      return
    end

    H1 { "Git LFS Project #{@project.name}" }

    UL {
      LI { 'Public: ' + (@project.public ? 'yes' : 'no') }
      LI { "Updated At: #{@project.updated_at}" }
      LI { "Created At: #{@project.created_at}" }
      LI {
        SPAN { 'Git LFS URL: ' }
        A(href: @project.lfs_url) { @project.lfs_url }
      }
      if !@editing_project
        LI {
          SPAN { 'Repository URL: ' }
          A(href: @project.repo_url, target: '_blank') { @project.repo_url }
        }
        LI { "Clone URL: #{@project.clone_url}" }
      else
        LI {
          SPAN { 'Repository URL: ' }
          RS.Input(type: :text, value: @editing_repo_url,
                   placeholder: 'Repo URL on Github').on(:change) { |e|
            mutate {
              @editing_repo_url = e.target.value
              @edited_project = true
            }
          }
        }
        LI {
          SPAN { 'Clone URL: ' }
          RS.Input(type: :text, value: @editing_clone_url,
                   placeholder: 'Repo clone URL from Github').on(:change) { |e|
            mutate {
              @editing_clone_url = e.target.value
              @edited_project = true
            }
          }
        }
      end
    }

    P { @editing_status_text } if @editing_status_text

    if !@editing_project
      if App.acting_user&.admin?
        RS.Button { 'Edit' }.on(:click) {
          mutate {
            @editing_repo_url = @project.repo_url
            @editing_clone_url = @project.clone_url
            @editing_project = true
          }
        }
        BR {}
      end
    else
      RS.Button(color: 'primary', disabled: !@edited_project) {
        SPAN { 'Save' }
        RS.Spinner(size: 'sm') if @show_editing_spinner
      }.on(:click) {
        finish_editing
      }
      RS.Button(color: 'danger') { 'Cancel' }.on(:click) {
        mutate @editing_project = false
      }
    end

    P { 'Visit your profile to find your LFS access token.' }

    H2 { 'Files' }
    P { "File tree generated at: #{@project.file_tree_updated || 'never'}" }

    breadcrumbs

    Paginator(current_page: @file_page,
              page_size: 100,
              item_count: files.count,
              ref: set(:paginator)) {
      # This is set with a delay
      if @paginator
        RS.Table(:striped, :responsive) {
          THEAD {
            TR {
              TH { 'Type' }
              TH { 'Name' }
              TH { 'Size' }
              TH { 'LFS?' }
            }
          }

          TBODY {
            files.paginated(@paginator.offset, @paginator.take_count).each { |file|
              # Skip root folder
              next if file.root?

              LFSGitFileItem(file: file, base_path: location.pathname).on(:navigate) { |path|
                change_folder path
              }
            }
          }
        }
      end
    }.on(:page_changed) { |page|
      mutate { @file_page = page }
    }.on(:created) {
      mutate {}
    }

    BR {}

    P { @refresh_message } if @refresh_message

    if App.acting_user&.developer?
      RS.Button(color: 'warning', disabled: @refresh_pressed) { 'Refresh' }.on(:click) {
        mutate {
          @refresh_pressed = true
        }

        RefreshGitFiles.run(project_id: @project.id).then {
          mutate {
            @refresh_message = 'Refresh queued. Please refresh this page in a minute' \
                               " if updates don't become visible"
          }
        }.fail { |error|
          mutate {
            @refresh_message = "Error: #{error}"
          }
        }
      }
    end

    if App.acting_user&.admin?
      RS.Button(color: 'danger', disabled: @rebuild_pressed) { 'Rebuild' }.on(:click) {
        mutate {
          @rebuild_pressed = true
        }

        RebuildGitFiles.run(project_id: @project.id).then {
          mutate {
            @refresh_message = 'Rebuild queued. Please refresh in a minute'
          }
        }.fail { |error|
          mutate {
            @refresh_message = "Error: #{error}"
          }
        }
      }
    end

    HR {}

    H2 { 'Statistics' }

    RS.Table(:bordered) {
      TBODY {
        TR {
          TH { 'Total size (MiB)' }
          TD { @project.total_size_mib.to_s }
        }

        TR {
          TH { 'Item count' }
          TD { (@project.total_object_count || 0).to_s }
        }
      }
    }

    P { "Statistics updated: #{@project.total_size_updated || 'never'}" }

    if App.acting_user&.developer?
      H3 { 'Raw Objects' }

      if @show_raw_objects
        Paginator(current_page: @CurrentRawPage,
                  item_count: raw_items.count,
                  show_totals: true,
                  ref: set(:paginator)) {
          # This is set with a delay
          if @paginator
            RS.Table(:striped, :reactive) {
              THEAD {
                TR {
                  TH { 'OID' }
                  TH { 'Size' }
                  TH { 'Created At' }
                }
              }

              TBODY {
                raw_items.paginated(@paginator.offset, @paginator.take_count).each { |object|
                  LFSObjectItem(object: object)
                }
              }
            }
          end
        }.on(:page_changed) { |page|
          mutate @CurrentRawPage = page
        }.on(:created) {
          mutate {}
        }
      else
        RS.Button(color: 'secondary') {
          'View raw Git LFS objects'
        }.on(:click) {
          mutate @show_raw_objects = true
        }
      end

      BR {}
      BR {}
    end
  end
end
