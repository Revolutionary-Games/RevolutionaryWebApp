# frozen_string_literal: true

module LfsOps
  # Rebuilds the git files of an lfs project
  class RebuildGitFiles < Hyperstack::ServerOp
    param :acting_user
    param :project_id
    add_error(:project_id, :does_not_exist, 'project does not exist') {
      !(@project = LfsProject.find_by_id(params.project_id))
    }
    validate { params.acting_user.admin? }
    step {
      RebuildLfsProjectFilesJob.perform_later @project.id
    }
  end
end
