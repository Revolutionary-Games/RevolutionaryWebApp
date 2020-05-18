# frozen_string_literal: true

# Updates the URLs on a project and triggers file rebuild
class UpdateLfsProjectUrls < Hyperstack::ServerOp
  param :acting_user
  param :project_id
  param :repo_url
  param :clone_url
  add_error(:project_id, :does_not_exist, 'project settings do not exist') {
    !(@project = LfsProject.find_by_id(params.project_id))
  }
  validate { params.acting_user.admin? }
  step {
    @project.repo_url = params.repo_url
    @project.clone_url = params.clone_url
  }
  step {
    @project.save!
  }
  step {
    RebuildLfsProjectFilesJob.perform_later @project.id
  }
end
