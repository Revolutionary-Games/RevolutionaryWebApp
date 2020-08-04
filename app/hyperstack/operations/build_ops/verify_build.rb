# frozen_string_literal: true

module BuildOps
  # Marks a build as verified
  class VerifyBuild < Hyperstack::ServerOp
    param :acting_user
    param :build_id
    param :verify_sibling
    validate { params.acting_user.developer? }
    add_error(:build_id, :does_not_exist, 'build does not exist') {
      !(@build = DevBuild.find_by_id(params.build_id))
    }
    step {
      Rails.logger.info "Build #{@build.id} verified by #{params.acting_user.email}"

      if params.verify_sibling
        @build.related.each { |other|
          Rails.logger.info "Also verifying related: #{other.id}"
          other.verified = true
          other.user = params.acting_user
          other.save!
        }
      end

      @build.verified = true
      @build.user = params.acting_user
      @build.save!
    }
  end
end
