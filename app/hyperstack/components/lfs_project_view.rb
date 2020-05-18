# frozen_string_literal: true

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

# Shows a single LFS project
class LfsProjectView < HyperComponent
  include Hyperstack::Router::Helpers

  param :current_raw_page, default: 0, type: Integer

  def raw_items
    LfsObject.by_project(@project.id).sort_by_created_at
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

    @project = LfsProject.find_by slug: match.params[:slug]
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
          A(href: @project.repo_url) { @project.repo_url }
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

    SPAN { @editing_status_text } if @editing_status_text

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

    H2 { 'Files' }
    P { "File tree generated at: #{@project.file_tree_updated || 'never'}" }
    P { 'TODO: some kind of list' }
  end
end
