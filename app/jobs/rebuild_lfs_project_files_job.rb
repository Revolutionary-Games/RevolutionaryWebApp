# frozen_string_literal: true

# Fully recreates the list of files for a lfs project
class RebuildLfsProjectFilesJob < ApplicationJob
  queue_as :default

  def perform(id)
    project = LfsProject.find_by_id id

    unless project
      logger.warning "Can't rebuild files for non-existant lfs project"
      return
    end

    puts 'TODO: rebuild files job'
  end
end
